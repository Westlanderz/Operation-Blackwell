using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

namespace OperationBlackwell.Core {
	public class GameController : Singleton<GameController> {
		private const bool DebugMovement = false;

		[Header("World data")]
		[SerializeField] private Vector3 gridWorldSize_;
		[SerializeField] private float cellSize_;
		[SerializeField] private bool drawGridLines_;

		[Header("Map visuals")]
		[SerializeField] private MovementTilemapVisual movementTilemapVisual_;
		[SerializeField] private MovementTilemapVisual arrowTilemapVisual_;
		[SerializeField] private MovementTilemapVisual selectorTilemapVisual_;
		[SerializeField] private MovementTilemapVisual attackRangeTilemapVisual_;
		private MovementTilemap movementTilemap_;
		private MovementTilemap arrowTilemap_;
		private MovementTilemap selectorTilemap_;
		private MovementTilemap attackRangeTilemap_;
		public Grid<Tilemap.Node> grid { get; private set; }
		public GridPathfinding gridPathfinding { get; private set; }
		public Tilemap tilemap { get; private set; }
		[SerializeField] private TilemapVisual tilemapVisual_;

		[Header("Puzzles")]
		[SerializeField] private List<PuzzleComplete> puzzleDestroyableObjects_;
		public System.EventHandler<PuzzleCompleteArgs> PuzzleEnded;
		public System.EventHandler<int> PuzzleCompleted;

		public class PuzzleCompleteArgs : System.EventArgs {
			public int id;
			public bool success;
		}

		[Header("Cursor")]
		public EventHandler<string> CursorChanged;

		[Header("LevelTransitions")]
		public EventHandler<LevelTransitionArgs> LevelTransitionStarted;
		public class LevelTransitionArgs : System.EventArgs {
			public string currentLevel;
			public string nextLevel;
			public int cutsceneIndex;
		}

		private void Start() {
			grid = new Grid<Tilemap.Node>((int)gridWorldSize_.x, (int)gridWorldSize_.y, cellSize_, new Vector3(0, 0, 0), 
				(Grid<Tilemap.Node> g, Vector3 worldPos, int x, int y) => new Tilemap.Node(worldPos, x, y, g, false, Tilemap.Node.wallHitChanceModifier, false), drawGridLines_);
			tilemap = new Tilemap(grid);
			Vector3 origin = new Vector3(0, 0);

			gridPathfinding = new GridPathfinding(origin + new Vector3(1, 1) * cellSize_ * .5f, new Vector3(gridWorldSize_.x, gridWorldSize_.y) * cellSize_, cellSize_);
			if(movementTilemapVisual_ != null) {
				movementTilemap_ = new MovementTilemap((int)gridWorldSize_.x, (int)gridWorldSize_.y, cellSize_, new Vector3(0, 0, 0));
				movementTilemap_.SetTilemapVisual(movementTilemapVisual_);
			}
			if(arrowTilemapVisual_ != null) {
				arrowTilemap_ = new MovementTilemap((int)gridWorldSize_.x, (int)gridWorldSize_.y, cellSize_, new Vector3(0, 0, 0));
				arrowTilemap_.SetTilemapVisual(arrowTilemapVisual_);
			}
			if(selectorTilemapVisual_ != null) {
				selectorTilemap_ = new MovementTilemap((int)gridWorldSize_.x, (int)gridWorldSize_.y, cellSize_, new Vector3(0, 0, 0));
				selectorTilemap_.SetTilemapVisual(selectorTilemapVisual_);
			}
			if(attackRangeTilemapVisual_ != null) {
				attackRangeTilemap_ = new MovementTilemap((int)gridWorldSize_.x, (int)gridWorldSize_.y, cellSize_, new Vector3(0, 0, 0));
				attackRangeTilemap_.SetTilemapVisual(attackRangeTilemapVisual_);
			}
			tilemap.SetTilemapVisual(tilemapVisual_);

			if(SceneManager.GetActiveScene().name == "TutorialLevel") {
				LoadTilemapData("tutorial_V4.5");
			} else if(SceneManager.GetActiveScene().name == "PrisonLevel") {
				// Nothing to load here.
			} else if(SceneManager.GetActiveScene().name == "Final Level") {
				LoadTilemapData("finallevel_V1.10");
			} else if(SceneManager.GetActiveScene().name == "Level3") {
				tilemap.Load("level3");
			} else {
				Debug.Log(SceneManager.GetActiveScene().name + " has no level to load!");
			}

			PuzzleEnded += OnPuzzleComplete;
			GridCombatSystem.instance.GameEnded += OnGameEnded;
		}

		private void Update() {
			HandleMisc();
		}

		public Grid<Tilemap.Node> GetGrid() {
			return grid;
		}

		public MovementTilemap GetMovementTilemap() {
			return movementTilemap_;
		}

		public MovementTilemap GetArrowTilemap() {
			return arrowTilemap_;
		}
		
		public MovementTilemap GetSelectorTilemap() {
			return selectorTilemap_;
		}

		public MovementTilemap GetAttackRangeTilemap() {
			return attackRangeTilemap_;
		}

		private void HandleMisc() {
			if(Input.GetKeyDown(KeyCode.Escape)) {
				CursorChanged?.Invoke(this, "Arrow");
				GlobalController.instance.ReturnMainMenu();
			}
		}

		private void OnPuzzleComplete(object sender, PuzzleCompleteArgs args) {
			foreach(PuzzleComplete puzzle in puzzleDestroyableObjects_) {
				if(puzzle.puzzleID == args.id && args.success) {
					foreach(GameObject destroyableObject in puzzle.destroyableObjects) {
						Destroy(destroyableObject);
					}
					PuzzleCompleted?.Invoke(this, args.id);
					break;
				}
			}
		}
		
		public void OnGameEnded(object sender, bool won) {
			if(won) {
				SceneManager.LoadScene("WinScreen");
			} else {
				SceneManager.LoadScene("LoseScreen");
			}
		}

		public void LoadTilemapData(string jsonFilePath) {
			tilemap.Load(jsonFilePath);
		}
	}
}

[System.Serializable]
public struct PuzzleComplete {
	public int puzzleID;
	public List<GameObject> destroyableObjects;
}
