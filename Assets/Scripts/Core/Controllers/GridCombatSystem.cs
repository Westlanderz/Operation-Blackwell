﻿using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System;

namespace OperationBlackwell.Core {
	public class GridCombatSystem : Singleton<GridCombatSystem> {
		// AI events
		public EventHandler<int> AIStageLoaded;
		public EventHandler<int> AIStageUnloaded;
		public EventHandler<EventArgs> AISetTurn;
		public EventHandler<EventArgs> AITurnSet;
		public EventHandler<EventArgs> StageLoaded;

		// Cutscene events
		public EventHandler<int> CutsceneTriggered;

		// Boss events
		public event Action BossStarted;
		public event Action BossEnded;
		public event Action BossReenabled;

		[Header("Units")]
		[SerializeField] private List<CoreUnit> blueTeamList_;

		private State state_;
		private CoreUnit unitGridCombat_;
		private List<CoreUnit> redTeamList_;

		private List<int> playedCutsceneIndexes_;

		private List<PathNode> currentPathUnit_;
		private int pathLength_;
		private int turn_;
		private WaitingQueue<OrderObject> orderList_;
		private IInteractable interactable_;

		public EventHandler<EventArgs> OnUnitDeath;
		public EventHandler<UnitPositionEvent> OnUnitSelect;
		public EventHandler<EventArgs> OnUnitDeselect;
		public EventHandler<UnitPositionEvent> OnUnitMove;
		public EventHandler<UnitEvent> OnUnitActionPointsChanged;
		public EventHandler<string> OnWeaponChanged;
		public EventHandler<int> OnTurnEnded;
		public EventHandler<bool> GameEnded;

		public class UnitEvent : EventArgs {
			public CoreUnit unit;
		}

		public class UnitPositionEvent : UnitEvent {
			public Vector3 position;
		}

		// All variables below are for optimization purposes. NEVER use them directly.
		private CoreUnit prevUnit_;
		private CoreUnit loadCamUnit_;
		private Tilemap.Node prevNode_;
		private Vector3 prevPosition_;
		private int prevActionCount_;
		private bool setAiTurn_;
		private bool firstUpdate_;
		private bool inBossFight_;

		public enum State {
			Normal,
			Boss,
			UnitSelected,
			EndingTurn,
			Waiting,
			Cutscene,
			Transition,
			OutOfCombat,
		}

		private enum MouseButtons {
			Leftclick,
			Rightclick,
			Middlemouseclick
		}

		private void Start() {
			turn_ = 1;
			redTeamList_ = new List<CoreUnit>();

			playedCutsceneIndexes_ = new List<int>();

			// Set all UnitGridCombat on their GridPosition
			foreach(CoreUnit unitGridCombat in blueTeamList_) {
				GameController.instance.GetGrid().GetGridObject(unitGridCombat.GetPosition())
					.SetUnitGridCombat(unitGridCombat);
			}

			orderList_ = new WaitingQueue<OrderObject>();

			prevUnit_ = null;
			prevNode_ = null;
			prevPosition_ = Vector3.zero;
			setAiTurn_ = true;

			state_ = State.OutOfCombat;
			OnUnitDeath += RemoveUnitOnDeath;
			AITurnSet += OnAITurnSet;
			StageLoaded += OnStageLoaded;

			firstUpdate_ = true;
			inBossFight_ = false;
		}

		private void OnDestroy() {
			OnUnitDeath -= RemoveUnitOnDeath;
			AITurnSet -= OnAITurnSet;
			StageLoaded -= OnStageLoaded;
		}

		public void LoadAllEnemies(List<CoreUnit> enemies) {
			foreach(CoreUnit enemy in enemies) {
				if(enemy.GetTeam() == Team.Red) {
					redTeamList_.Add(enemy);
				}
			}
		}

		public List<CoreUnit> GetBlueTeam() {
			return blueTeamList_;
		}

		public void AddToTeam(CoreUnit unit) {
			if(unit.GetTeam() == Team.Blue) {
				blueTeamList_.Add(unit);
			}
		}

		private void RemoveUnitOnDeath(object sender, EventArgs e) {
			CoreUnit unit = (CoreUnit)sender;
			if(unit.GetTeam() == Team.Blue) {
				blueTeamList_.Remove(unit);
				state_ = State.Waiting;
			} else {
				redTeamList_.Remove(unit);
				if(redTeamList_.Count <= 0 && state_ != State.Boss) {
					AIStageUnloaded?.Invoke(this, 0);
					state_ = State.OutOfCombat;
				} else if(redTeamList_.Count <= 1 && state_ == State.Boss) {
					BossReenabled?.Invoke();
				}
			}
			orderList_.GetQueue().RemoveAll(x => x.GetUnit() == unit);
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Tilemap.Node gridObject = grid.GetGridObject(unit.GetPosition());
			gridObject.ClearUnitGridCombat();
		}

		public void UpdateValidMovePositions(Vector3 position) {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			GridPathfinding gridPathfinding = GameController.instance.gridPathfinding;

			// Get Unit Grid Position X, Y
			grid.GetXY(position, out int unitX, out int unitY);

			int maxMoveDistance = unitGridCombat_.GetActionPoints() / 2;
			for(int x = unitX - maxMoveDistance; x <= unitX + maxMoveDistance; x++) {
				for(int y = unitY - maxMoveDistance; y <= unitY + maxMoveDistance; y++) {
					if(x < 0 || x >= grid.GetWidth() || y < 0 || y >= grid.GetHeight()) {
						continue;
					}

					if(gridPathfinding.IsWalkable(x, y) && x != unitX || y != unitY) {
						int length = gridPathfinding.GetPath(unitX, unitY, x, y).Count;
						// Position is Walkable
						if(length > 0 && length <= maxMoveDistance) {
							// There is a Path
								
							// Set Tilemap Tile to Move
							GameController.instance.GetMovementTilemap().SetTilemapSprite(
								x, y, MovementTilemap.TilemapObject.TilemapSprite.Move
							);

							grid.GetGridObject(x, y).SetIsValidMovePosition(true);
						}
					}
				}
			}
		}

		private void LateUpdate() {
			if(state_ == State.Cutscene || state_ == State.Transition) {
				GameController.instance.CursorChanged?.Invoke(this, "Arrow");
				return;
			}
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Tilemap.Node gridObject = grid.GetGridObject(Utils.GetMouseWorldPosition());
			List<Actions> actions = new List<Actions>();
			if(unitGridCombat_ != null) {
				actions = unitGridCombat_.LoadActions().GetQueue();
			}
			
			if(gridObject != null) {
				CoreUnit unit = gridObject.GetUnitGridCombat();
				if(actions.Count == 0) {
					if(unit != null && unitGridCombat_ != null && unitGridCombat_.CanAttackUnit(unit, unitGridCombat_.GetPosition()) && state_ == State.UnitSelected) {
						GameController.instance.CursorChanged?.Invoke(this, "Attack");
					} else if(gridObject.GetIsValidMovePosition() && unitGridCombat_ != null && (state_ == State.UnitSelected || state_ == State.OutOfCombat)) {
						GameController.instance.CursorChanged?.Invoke(this, "Move");
					} else if(unit != null && unit.GetTeam() == Team.Blue && (state_ == State.Normal || state_ == State.UnitSelected || state_ == State.OutOfCombat)) {
						GameController.instance.CursorChanged?.Invoke(this, "Select");
					} else {
						GameController.instance.CursorChanged?.Invoke(this, "Arrow");
					}
					if(unitGridCombat_ != null) {
						OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
							unit = unitGridCombat_,
							position = unitGridCombat_.GetPosition()
						});
					}
				} else if(actions.Count > 0) {
					if(unit != null && unitGridCombat_ != null && unitGridCombat_.CanAttackUnit(unit, actions[actions.Count - 1].destinationPos) && state_ == State.UnitSelected) {
						GameController.instance.CursorChanged?.Invoke(this, "Attack");
					} else if(gridObject.GetIsValidMovePosition() && unitGridCombat_ != null && (state_ == State.UnitSelected || state_ == State.OutOfCombat)) {
						GameController.instance.CursorChanged?.Invoke(this, "Move");
					} else if(unit != null && unit.GetTeam() == Team.Blue && (state_ == State.Normal || state_ == State.UnitSelected || state_ == State.OutOfCombat)) {
						GameController.instance.CursorChanged?.Invoke(this, "Select");
					} else {
						GameController.instance.CursorChanged?.Invoke(this, "Arrow");
					}
					if(unitGridCombat_ != null) {
						OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
							unit = unitGridCombat_,
							position = actions[actions.Count - 1].destinationPos
						});
					}
				}
			} else {
				GameController.instance.CursorChanged?.Invoke(this, "Arrow");
			}
		}

		private void Update() {
			if(firstUpdate_) {
				firstUpdate_ = false;
				CheckTriggers();
			}
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Tilemap.Node gridObject = grid.GetGridObject(Utils.GetMouseWorldPosition());
			CoreUnit unit;

			if((state_ == State.Cutscene || state_ == State.Transition) && prevNode_ != null) {
				GameController.instance.GetSelectorTilemap().SetTilemapSprite(
					prevNode_.gridX, prevNode_.gridY, MovementTilemap.TilemapObject.TilemapSprite.None
				);
				return;
			}

			if(gridObject == null) {
				if(prevNode_ != null) {
					GameController.instance.GetSelectorTilemap().SetTilemapSprite(
						prevNode_.gridX, prevNode_.gridY, MovementTilemap.TilemapObject.TilemapSprite.None
					);
				}
				prevNode_ = gridObject;
				return;
			}

			if(prevNode_ != null && prevNode_ != gridObject) {
				GameController.instance.GetSelectorTilemap().SetTilemapSprite(
					prevNode_.gridX, prevNode_.gridY, MovementTilemap.TilemapObject.TilemapSprite.None
				);
				GameController.instance.GetSelectorTilemap().SetTilemapSprite(
					gridObject.gridX, gridObject.gridY, MovementTilemap.TilemapObject.TilemapSprite.Move
				);
			}
			switch(state_) {
				case State.Normal:
				case State.Boss:
					interactable_ = null;
					
					unit = gridObject.GetUnitGridCombat();
					if(unit != null && unit.GetTeam() == Team.Blue) {
						if(Input.GetMouseButtonDown((int)MouseButtons.Leftclick)) {
							OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
								unit = unit,
								position = unit.GetPosition()
							});
							unitGridCombat_ = unit;
							state_ = State.UnitSelected;
						}
					}
					break;
				case State.UnitSelected:
					UnitEvent unitEvent = new UnitEvent() {
						unit = unitGridCombat_
					};
					OnUnitActionPointsChanged?.Invoke(this, unitEvent);
					loadCamUnit_ = unitGridCombat_;

					interactable_ = null;
					
					if(gridObject == null) {
						prevNode_ = gridObject;
						return;
					}

					// Set arrow to target position
					List<Actions> actions = unitGridCombat_.LoadActions().GetQueue();
					if(unitGridCombat_ != null && actions.Count == 0) {
						if(prevPosition_ != unitGridCombat_.GetPosition() || (prevUnit_ != unitGridCombat_ && prevUnit_ != null)) {
							ResetMoveTiles();
							ResetAttackTiles();
							int maxMoveDistance = unitGridCombat_.GetActionPoints() / 2;
							int attackRange = unitGridCombat_.GetAttackRange();
							bool canAttack = unitGridCombat_.GetAttackMaxCost() <= unitGridCombat_.GetActionPoints();
							if(attackRange <= maxMoveDistance) {
								UpdateValidMovePositions(unitGridCombat_.GetPosition());
								if(canAttack) {
									SetAttackRangeVisual(unitGridCombat_.GetPosition());
								}
							} else {
								if(canAttack) {
									SetAttackRangeVisual(unitGridCombat_.GetPosition());
								}
								UpdateValidMovePositions(unitGridCombat_.GetPosition());
							}
						}
						if(prevNode_ != null && prevNode_ != gridObject) {
							ResetArrowVisual();
							if(gridObject.GetIsValidMovePosition()) {
								SetArrowWithPath(Vector3.zero, Vector3.zero);
							}
						}
					} else {
						if(prevActionCount_ != actions.Count) {
							foreach(Actions action in actions) {
								if(action.type == Actions.ActionType.Move) {
									SetArrowWithPath(action.originPos, action.destinationPos);
								}
							}
							ResetMoveTiles();
							ResetAttackTiles();
							int maxMoveDistance = unitGridCombat_.GetActionPoints() / 2;
							int attackRange = unitGridCombat_.GetAttackRange();
							bool canAttack = unitGridCombat_.GetAttackMaxCost() <= unitGridCombat_.GetActionPoints();
							if(attackRange <= maxMoveDistance) {
								UpdateValidMovePositions(actions[actions.Count - 1].destinationPos);
								if(canAttack) {
									SetAttackRangeVisual(actions[actions.Count - 1].destinationPos);
								}
							} else {
								if(canAttack) {
									SetAttackRangeVisual(actions[actions.Count - 1].destinationPos);
								}
								UpdateValidMovePositions(actions[actions.Count - 1].destinationPos);
							}
						}
						if(prevNode_ != null && prevNode_ != gridObject) {
							ResetArrowVisual();
							if(gridObject.GetIsValidMovePosition() && actions.Count != 0) {
								SetArrowWithPath(actions[actions.Count - 1].destinationPos, Utils.GetMouseWorldPosition());
							}
						}
					}

					unit = gridObject.GetUnitGridCombat();
					if(unit != null && unit.GetTeam() == Team.Blue) {
						if(Input.GetMouseButtonDown((int)MouseButtons.Leftclick)) {
							OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
								unit = unit,
								position = unit.GetPosition()
							});
							unitGridCombat_ = unit;
							state_ = State.UnitSelected;
						}
					} else if(gridObject.GetIsValidMovePosition()) {
						// Draw arrow acording to pathfinding path
						if(Input.GetMouseButtonDown((int)MouseButtons.Rightclick)) {
							// Save the actions for the unit
							if(gridObject.GetIsValidMovePosition()) {
								// Valid Move Position
								if(unitGridCombat_.HasActionPoints()) {
									state_ = State.Waiting;

									// Set entire Tilemap to Invisible
									GameController.instance.GetMovementTilemap().SetAllTilemapSprite(
										MovementTilemap.TilemapObject.TilemapSprite.None
									);
									GameController.instance.CursorChanged?.Invoke(this, "Arrow");

									Actions unitAction;
									if(actions.Count == 0) {
										pathLength_ = GameController.instance.gridPathfinding.GetPath(unitGridCombat_.GetPosition(), Utils.GetMouseWorldPosition()).Count * 2;
										unitAction = new Actions(Actions.ActionType.Move, gridObject, Utils.GetMouseWorldPosition(),
											grid.GetGridObject(unitGridCombat_.GetPosition()), unitGridCombat_.GetPosition(), unitGridCombat_, null, pathLength_);
									} else {
										pathLength_ = GameController.instance.gridPathfinding.GetPath(actions[actions.Count - 1].destinationPos, Utils.GetMouseWorldPosition()).Count * 2;
										unitAction = new Actions(Actions.ActionType.Move, gridObject, Utils.GetMouseWorldPosition(),
											grid.GetGridObject(actions[actions.Count - 1].destinationPos), actions[actions.Count - 1].destinationPos, unitGridCombat_, null, pathLength_);
									}
									unitGridCombat_.SaveAction(unitAction);

									OrderObject unitOrder = GetOrderObject(unitGridCombat_);
									if(unitOrder == null) {
										int cost = GenerateTotalCost(0, pathLength_, 0);
										int initiative = GenerateInitiative(cost, pathLength_, 0);
										unitOrder = new OrderObject(initiative, unitGridCombat_, cost);
										orderList_.Enqueue(unitOrder);
									} else {
										int newCost = GenerateTotalCost(unitOrder.GetTotalCost(), pathLength_, 0);
										int newInitiative = GenerateInitiative(newCost, pathLength_, 0);
										unitOrder.SetTotalCost(newCost);
										unitOrder.SetInitiative(newInitiative);
									}
									if(UnitsHaveActionsPoints()) {
										state_ = State.UnitSelected;
									} else {
										state_ = State.EndingTurn;
									}
								}
							}
						} else if(Input.GetKeyDown(KeyCode.F)) {
							DeselectUnit();
							if(inBossFight_) {
								state_ = State.Boss;
							} else {
								state_ = State.Normal;;
							}
						}
					} else if(gridObject.GetUnitGridCombat() != null && gridObject.GetUnitGridCombat().GetTeam() != Team.Blue) {
						if(Input.GetMouseButtonDown((int)MouseButtons.Rightclick)) {
							// Can Attack Enemy
							int attackCost = unitGridCombat_.GetAttackCost();
							if(unitGridCombat_.GetActionPoints() >= attackCost) {
								// Attack Enemy
								state_ = State.Waiting;
								Actions.AttackType attackType = unitGridCombat_.GetAttackType();
								Actions unitAction = null;
								if(actions.Count == 0) {
									if(unitGridCombat_.CanAttackUnit(unit, unitGridCombat_.GetPosition())) {
										unitAction = new Actions(Actions.ActionType.Attack, attackType, grid.GetGridObject(unitGridCombat_.GetPosition()), unitGridCombat_.GetPosition(),
											grid.GetGridObject(unitGridCombat_.GetPosition()), unitGridCombat_.GetPosition(), unitGridCombat_, unit, attackCost);
									}
								} else {
									if(unitGridCombat_.CanAttackUnit(unit, actions[actions.Count - 1].destinationPos)) {
										unitAction = new Actions(Actions.ActionType.Attack, attackType, actions[actions.Count - 1].destination, actions[actions.Count - 1].destinationPos,
											actions[actions.Count - 1].destination, actions[actions.Count - 1].destinationPos, unitGridCombat_, unit, attackCost);
									}
								}
								if(unitAction != null) {
									unitGridCombat_.SaveAction(unitAction);
									prevActionCount_++;
									OrderObject unitOrder = GetOrderObject(unitGridCombat_);
									if(unitOrder == null) {
										int cost = GenerateTotalCost(0, 0, attackCost);
										int initiative = GenerateInitiative(cost, 0, attackCost);
										unitOrder = new OrderObject(initiative, unitGridCombat_, cost);
										orderList_.Enqueue(unitOrder);
									} else {
										int newCost = GenerateTotalCost(unitOrder.GetTotalCost(), 0, 0);
										int newInitiative = GenerateInitiative(newCost, 0, 0);
										unitOrder.SetTotalCost(newCost);
										unitOrder.SetInitiative(newInitiative);
									}
								}
								
								if(UnitsHaveActionsPoints()) {
									state_ = State.UnitSelected;
								} else {
									state_ = State.EndingTurn;
								}
							}
						}
					} else if(!gridObject.GetIsValidMovePosition()) {
						interactable_ = gridObject.GetInteractable();
						if(interactable_ != null) {
							Vector3 interactablePosition;
							if(actions.Count == 0) {
								interactablePosition = unitGridCombat_.GetPosition();
							} else {
								interactablePosition = actions[actions.Count - 1].destinationPos;
							}
							if(Input.GetMouseButtonDown((int)MouseButtons.Rightclick) && interactable_.IsInRange(interactablePosition)) {
								Actions unitAction;
								int interactCost = interactable_.GetCost();
								if(actions.Count == 0) {
									unitAction = new Actions(Actions.ActionType.Interact, gridObject, Utils.GetMouseWorldPosition(),
										grid.GetGridObject(unitGridCombat_.GetPosition()), unitGridCombat_.GetPosition(), unitGridCombat_, null, interactCost);
								} else {
									unitAction = new Actions(Actions.ActionType.Interact, gridObject, Utils.GetMouseWorldPosition(),
										grid.GetGridObject(actions[actions.Count - 1].destinationPos), actions[actions.Count - 1].destinationPos, unitGridCombat_, null, interactCost);
								}
								unitGridCombat_.SaveAction(unitAction);
								OrderObject unitOrder = GetOrderObject(unitGridCombat_);
								if(unitOrder == null) {
									int cost = GenerateTotalCost(0, interactCost, 0);
									int initiative = GenerateInitiative(cost, interactCost, 0);
									unitOrder = new OrderObject(initiative, unitGridCombat_, cost);
									orderList_.Enqueue(unitOrder);
								} else {
									int newCost = GenerateTotalCost(unitOrder.GetTotalCost(), interactCost, 0);
									int newInitiative = GenerateInitiative(newCost, interactCost, 0);
									unitOrder.SetTotalCost(newCost);
									unitOrder.SetInitiative(newInitiative);
								}
								state_ = State.EndingTurn;
							}
						}
						if(Input.GetKeyDown(KeyCode.F)) {
							DeselectUnit();
							if(inBossFight_) {
								state_ = State.Boss;
							} else {
								state_ = State.Normal;;
							}
						}
					}
					break;
				case State.EndingTurn:
					ForceTurnOver();
					break;
				case State.OutOfCombat:
					interactable_ = null;

					if(gridObject == null) {
						prevNode_ = gridObject;
						return;
					}
					
					unit = gridObject.GetUnitGridCombat();
					if(Input.GetMouseButtonDown((int)MouseButtons.Rightclick)) {
						DeselectUnit();
					}
					if(unit != null && unit.GetTeam() == Team.Blue) {
						if(Input.GetMouseButtonDown((int)MouseButtons.Leftclick)) {
							if(unit != null && unit.GetTeam() == Team.Blue) {
								OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
									unit = unit,
									position = unit.GetPosition()
								});
								unitGridCombat_ = unit;
								OnUnitActionPointsChanged?.Invoke(this, new UnitEvent() {
									unit = unit,
								});
							}
						}
					}
					if(unitGridCombat_ == null) {
						prevNode_ = gridObject;
						return;
					}
					if(prevPosition_ != unitGridCombat_.GetPosition() || (prevUnit_ != unitGridCombat_ && prevUnit_ != null)) {
						ResetMoveTiles();
						UpdateValidMovePositions(unitGridCombat_.GetPosition());
					}
					if(prevNode_ != null && prevNode_ != gridObject) {
						ResetArrowVisual();
						if(gridObject.GetIsValidMovePosition()) {
							SetArrowWithPath(Vector3.zero, Vector3.zero);
						}
					}
					if(Input.GetMouseButtonDown((int)MouseButtons.Leftclick)) {
						if(gridObject.GetIsValidMovePosition()) {
							CoreUnit unitB = unitGridCombat_;
							Vector3 moveFromPos = unitB.GetPosition();
							grid.GetGridObject(unitB.GetPosition()).ClearUnitGridCombat();
							prevPosition_ = unitGridCombat_.GetPosition();
							unitB.MoveTo(Utils.GetMouseWorldPosition(), moveFromPos, () => {
								Tilemap.Node node = grid.GetGridObject(unitB.GetPosition());
								node.SetUnitGridCombat(unitB);
								CheckTriggers();
								CheckLevelTransition();
								CheckBossTrigger();
							});
							DeselectUnit();
						} else if(gridObject.GetInteractable() != null) {
							interactable_ = gridObject.GetInteractable();
							if(interactable_.IsInRange(unitGridCombat_.GetPosition())) {
								if(interactable_ is PuzzleTrigger puzzleTrigger) {
									prevPosition_ = Vector3.zero;
									puzzleTrigger.Interact(unitGridCombat_);
								} else {
									prevPosition_ = unitGridCombat_.GetPosition();
									interactable_.Interact(unitGridCombat_);
								}
								CheckTriggers();
								CheckLevelTransition();
								CheckBossTrigger();
							}
							DeselectUnit();
						}
					}
					break;
				default:
					break;
			}

			prevNode_ = gridObject;
			prevUnit_ = unitGridCombat_;
			if(unitGridCombat_ != null) {
				prevActionCount_ = unitGridCombat_.GetActionCount() - 1;
			} else {
				prevActionCount_ = 0;
			}

			if(state_ != State.OutOfCombat && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) {
				// End Turn
				state_ = State.EndingTurn;
				return;
			}
			if(Input.GetKeyDown(KeyCode.R)) {
				if(unitGridCombat_ != null) {
					unitGridCombat_.ClearActions();
					prevActionCount_ = unitGridCombat_.GetActionCount();
				}
			}
		}

		private void DeselectUnit() {
			unitGridCombat_ = null;
			ResetMoveTiles();
			ResetAttackTiles();
			ResetArrowVisual();
			UnitEvent unitEvent = new UnitEvent() {
				unit = unitGridCombat_
			};
			OnUnitActionPointsChanged?.Invoke(this, unitEvent);
			OnUnitDeselect?.Invoke(this, System.EventArgs.Empty);
		}

		private void ForceTurnOver() {
			// Execute all unit actions and end turn
			DeselectUnit();
			if(setAiTurn_) {
				setAiTurn_ = false;
				AISetTurn?.Invoke(this, System.EventArgs.Empty);
			}
		}

		private void ExecuteAllActions() {
			StartCoroutine(ExecuteAllActionsCoroutine());
		}

		IEnumerator ExecuteAllActionsCoroutine() {
			if(inBossFight_) {
				state_ = State.Boss;
			} else {
				state_ = State.Normal;;
			}
			bool hasExecuted = false;
			bool isComplete = false;
			// Sort the orderlist queue by initiative
			orderList_.Sort();
			while(!orderList_.IsEmpty()) {
				hasExecuted = orderList_.Peek().HasExecuted();
				isComplete = orderList_.Peek().IsComplete();
				if(!hasExecuted) {
					orderList_.Peek().ExecuteActions();
					CoreUnit unit = orderList_.Peek().GetUnit();
					OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
						unit = unit,
						position = unit.GetPosition()
					});
				} 
				if(isComplete) {
					orderList_.Dequeue();
				}
				yield return null;
			}
			orderList_.Clear();
			foreach(CoreUnit unit in blueTeamList_) {
				unit.ResetComplete();
			}
			foreach(CoreUnit unit in redTeamList_) {
				unit.ResetComplete();
			}
			ResetAllActionPoints();
			turn_++;
			OnTurnEnded?.Invoke(this, turn_);
			OnUnitSelect?.Invoke(this, new UnitPositionEvent() {
				unit = loadCamUnit_,
				position = loadCamUnit_.GetPosition()
			});
			setAiTurn_ = true;
			CheckTriggers();
		}

		public void SetState(State state) {
			state_ = state;
		}

		public State GetState() {
			return state_;
		}

		private void ResetAllActionPoints() {
			foreach(CoreUnit unit in blueTeamList_) {
				unit.ResetActionPoints();
			}
			foreach(CoreUnit unit in redTeamList_) {
				unit.ResetActionPoints();
			}
		}

		private void ResetMoveTiles() {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			// Reset Entire Grid ValidMovePositions
			for(int x = 0; x < grid.GetWidth(); x++) {
				for(int y = 0; y < grid.GetHeight(); y++) {
					grid.GetGridObject(x, y).SetIsValidMovePosition(false);
					GameController.instance.GetMovementTilemap().SetTilemapSprite(x, y, MovementTilemap.TilemapObject.TilemapSprite.None);
				}
			}
		}

		/*
		 * This function generates the totalcost of the units actions.
		 * As params the function needs the current cost, the pathlength and the range of the attack.
		 * The function has the ability to add modifiers to the totalcost calculation.
		 */
		private int GenerateTotalCost(int cost, int pathLength, int attackRange) {
			int totalCost = cost + pathLength + attackRange;
			return totalCost;
		}

		private int GenerateInitiative(int cost, int pathLength, int attackRange) {
			int initiative = UnityEngine.Random.Range(1, 10);
			return initiative;
		}

		public WaitingQueue<OrderObject> GetOrderList() {
			return orderList_;
		}

		private OrderObject GetOrderObject(CoreUnit unit) {
			if(orderList_ == null || orderList_.IsEmpty()) {
				return null;
			}

			foreach(OrderObject order in orderList_.GetQueue()) {
				if(order.GetUnit() == unit) {
					return order;
				}
			}

			return null;
		}

		private bool UnitsHaveActionsPoints() {
			foreach(CoreUnit unit in blueTeamList_) {
				if(unit.HasActionPoints() && unit.GetActionPoints() > 1) {
					return true;
				}
			}
			return false;
		}

		public CoreUnit GetActiveUnit() {
			return unitGridCombat_;
		}

		private void CheckTriggers() {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Vector3 position;
			Tilemap.Node node;
			TriggerNode trigger;
			foreach(CoreUnit unit in blueTeamList_) {
				position = unit.GetPosition();
				node = grid.GetGridObject(position);
				trigger = node.GetTrigger();
				if(trigger == null) {
					continue;
				}
				if(trigger.GetTrigger() != TriggerNode.Trigger.None) {
					if(trigger.GetTrigger() == TriggerNode.Trigger.Cutscene && !playedCutsceneIndexes_.Contains(trigger.GetIndex()) && state_ == State.OutOfCombat) {
						CutsceneTriggered?.Invoke(this, trigger.GetIndex());
						playedCutsceneIndexes_.Add(trigger.GetIndex());
						state_ = State.Cutscene;
					} else if(trigger.GetTrigger() == TriggerNode.Trigger.Combat) {
						AIStageLoaded?.Invoke(this, trigger.GetIndex());
					}
				}
			}
		}

		// If the start and end are not needed from the actionpath then it uses the selected player position and mouseposition for its calculation.
		private void SetArrowWithPath(Vector3 start, Vector3 end) {
			if(start == Vector3.zero && end == Vector3.zero) {
				start = unitGridCombat_.GetPosition();
				end = Utils.GetMouseWorldPosition();
			}
			currentPathUnit_ = GameController.instance.gridPathfinding.GetPath(start, end);
			MovementTilemap arrowMap = GameController.instance.GetArrowTilemap();
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			List<Actions> actions = unitGridCombat_.LoadActions().GetQueue();
			int x = 0, y = 0;
			foreach(PathNode node in currentPathUnit_) {
				x = node.xPos;
				y = node.yPos;

				if(grid.GetGridObject(x, y).GetUnitGridCombat() != unitGridCombat_ && grid.GetGridObject(x, y) != grid.GetGridObject(start)) {
					if(grid.GetGridObject(x, y) != grid.GetGridObject(end)) {
						if((node.parent.xPos > x || node.parent.xPos < x) && node.parent.yPos == y) {
							arrowMap.SetRotation(x, y, 90f);
							arrowMap.SetTilemapSprite(x, y, MovementTilemap.TilemapObject.TilemapSprite.ArrowStraight);
						} 
						if((node.parent.yPos > y || node.parent.yPos < y) && node.parent.xPos == x) {
							arrowMap.SetRotation(x, y, 0f);
							arrowMap.SetTilemapSprite(x, y, MovementTilemap.TilemapObject.TilemapSprite.ArrowStraight);
						}
					} else {
						if(node.parent.xPos > x && node.parent.yPos == y) {
							arrowMap.SetRotation(x, y, 90f);
						} else if(node.parent.xPos < x && node.parent.yPos == y) {
							arrowMap.SetRotation(x, y, -90f);
						} else if(node.parent.xPos == x && node.parent.yPos > y) {
							arrowMap.SetRotation(x, y, 180f);
						} else if(node.parent.xPos == x && node.parent.yPos < y) {
							arrowMap.SetRotation(x, y, 0f);
						}
						arrowMap.SetTilemapSprite(x, y, MovementTilemap.TilemapObject.TilemapSprite.ArrowEnd);
					}

					if(node.parent.parent == null) {
						continue;
					}

					if((node.parent.parent.xPos == node.parent.xPos && node.parent.xPos < x 
						&& node.parent.parent.yPos > node.parent.yPos && node.parent.yPos == y)
						|| (node.parent.parent.xPos > node.parent.xPos && node.parent.xPos == x 
						&& node.parent.parent.yPos == node.parent.yPos && node.parent.yPos < y)) {
						arrowMap.SetRotation(node.parent.xPos, node.parent.yPos, 90f);
						arrowMap.SetTilemapSprite(node.parent.xPos, node.parent.yPos, MovementTilemap.TilemapObject.TilemapSprite.ArrowCorner);
					}
					if((node.parent.parent.xPos == node.parent.xPos && node.parent.xPos > x 
						&& node.parent.parent.yPos > node.parent.yPos && node.parent.yPos == y)
						|| (node.parent.parent.xPos < node.parent.xPos && node.parent.xPos == x 
						&& node.parent.parent.yPos == node.parent.yPos && node.parent.yPos < y)) {
						arrowMap.SetRotation(node.parent.xPos, node.parent.yPos, 180f);
						arrowMap.SetTilemapSprite(node.parent.xPos, node.parent.yPos, MovementTilemap.TilemapObject.TilemapSprite.ArrowCorner);
					}
					if((node.parent.parent.xPos == node.parent.xPos && node.parent.xPos < x 
						&& node.parent.parent.yPos < node.parent.yPos && node.parent.yPos == y)
						|| (node.parent.parent.xPos > node.parent.xPos && node.parent.xPos == x 
						&& node.parent.parent.yPos == node.parent.yPos && node.parent.yPos > y)) {
						arrowMap.SetRotation(node.parent.xPos, node.parent.yPos, 0f);
						arrowMap.SetTilemapSprite(node.parent.xPos, node.parent.yPos, MovementTilemap.TilemapObject.TilemapSprite.ArrowCorner);
					}
					if((node.parent.parent.xPos == node.parent.xPos && node.parent.xPos > x 
						&& node.parent.parent.yPos < node.parent.yPos && node.parent.yPos == y)
						|| (node.parent.parent.xPos < node.parent.xPos && node.parent.xPos == x 
						&& node.parent.parent.yPos == node.parent.yPos && node.parent.yPos > y)) {
						arrowMap.SetRotation(node.parent.xPos, node.parent.yPos, -90f);
						arrowMap.SetTilemapSprite(node.parent.xPos, node.parent.yPos, MovementTilemap.TilemapObject.TilemapSprite.ArrowCorner);
					}
				}
				if(actions.Count != 0 && node.parent != null) {
					
				}
			}
		}

		private void ResetArrowVisual() {
			GameController.instance.GetArrowTilemap().SetAllTilemapSprite(MovementTilemap.TilemapObject.TilemapSprite.None);
		}

		public void AddOrderObject(OrderObject orderObj) {
			orderList_.Enqueue(orderObj);
		}

		private void OnAITurnSet(object sender, EventArgs e) {
			ExecuteAllActions();
		}

		private void CheckLevelTransition() {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Vector3 position;
			Tilemap.Node node;
			LevelTransitionTrigger trigger;
			foreach(CoreUnit unit in blueTeamList_) {
				position = unit.GetPosition();
				node = grid.GetGridObject(position);
				trigger = node.GetLevelTransitionTrigger();
				if(trigger == null) {
					continue;
				}
				GameController.instance.LevelTransitionStarted?.Invoke(this, new GameController.LevelTransitionArgs {
					currentLevel = trigger.GetCurrentLevel(),
					nextLevel = trigger.GetNextLevel(),
					cutsceneIndex = trigger.GetCutsceneIndex()
				});

				DeselectUnit();

				state_ = State.Transition;
				break;
			}
		}

		private void CheckBossTrigger() {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			Vector3 position;
			Tilemap.Node node;
			BossTrigger trigger;
			foreach(CoreUnit unit in blueTeamList_) {
				position = unit.GetPosition();
				node = grid.GetGridObject(position);
				trigger = node.GetBossTrigger();
				if(trigger == null) {
					continue;
				}

				BossStarted?.Invoke();

				DeselectUnit();

				state_ = State.Boss;
				inBossFight_ = true;
				break;
			}
		}

		public void EndBossStages() {
			BossEnded?.Invoke();
			inBossFight_ = false;
			state_ = State.OutOfCombat;
		}

		private void OnStageLoaded(object sender, EventArgs e) {
			state_ = State.Normal;
		}

		public void SetActiveWeapon(int index) {
			if(state_ == State.Transition || state_ == State.OutOfCombat || state_ == State.Cutscene || unitGridCombat_ == null) {
				return;
			}
			unitGridCombat_.SetActiveWeapon(index);
		}

		private void SetAttackRangeVisual(Vector3 position) {
			Grid<Tilemap.Node> grid = GameController.instance.GetGrid();
			GridPathfinding gridPathfinding = GameController.instance.gridPathfinding;

			// Get Unit Grid Position X, Y
			grid.GetXY(position, out int unitX, out int unitY);

			int maxRange = unitGridCombat_.GetAttackRange();
			for(int x = unitX - maxRange; x <= unitX + maxRange; x++) {
				for(int y = unitY - maxRange; y <= unitY + maxRange; y++) {
					if(x < 0 || x >= grid.GetWidth() || y < 0 || y >= grid.GetHeight()) {
						continue;
					}

					if(x != unitX || y != unitY) {	
						// Set Tilemap Tile to Move
						GameController.instance.GetAttackRangeTilemap().SetTilemapSprite(
							x, y, MovementTilemap.TilemapObject.TilemapSprite.Move
						);
					}
				}
			}
		}

		private void ResetAttackTiles() {
			GameController.instance.GetAttackRangeTilemap().SetAllTilemapSprite(MovementTilemap.TilemapObject.TilemapSprite.None);
		}
	}
}
