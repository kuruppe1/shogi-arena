// ==========================================
// GameLogic.cs - 詰み判定修正版
// 修正点: 
// 1. 手が完了した直後に相手の詰みをチェック
// 2. 詰み判定のタイミングを改善
// ==========================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ==========================================================================
// GameState enum定義（Check、Checkmate追加版）
// ==========================================================================
public enum GameState
{
    NotStarted,
    InProgress,
    Check,        // 王手状態
    Checkmate,    // 詰み状態
    SenteWin,
    GoteWin,
    Draw,
    Paused,
    Aborted
}

// ==========================================================================
// 品質レベル定義（Unity互換）
// ==========================================================================
public enum Quality
{
    Low = 0,
    Medium = 1,
    High = 2,
    VeryHigh = 3,
    Ultra = 4
}

// ==========================================================================
// 型定義クラス群
// ==========================================================================

/// <summary>
/// ゲーム設定クラス（高機能版 - 全Manager互換）
/// </summary>
[System.Serializable]
public class GameSettings
{
    [Header("ゲーム基本設定")]
    public float moveTimeLimit = 30f;
    public bool enableLogging = true;
    public bool enableSoundEffects = true;
    public bool enableMoveAnimation = true;
    public int difficulty = 1;
    public string playerName = "Player";
    
    [Header("音声設定")]
    public bool enableAudio = true;
    public float masterVolume = 1.0f;
    public float bgmVolume = 0.8f;
    public float sfxVolume = 1.0f;
    public float voiceVolume = 1.0f;
    public float uiVolume = 0.9f;
    
    [Header("グラフィック設定")]
    public Quality graphicsQuality = Quality.High;
    public int targetFrameRate = 60;
    public bool enableVSync = true;
    public bool enableAntiAliasing = true;
    public bool enableShadows = true;
    public bool enableParticles = true;
    
    [Header("UI設定")]
    public bool enableAnimations = true;
    public float uiScale = 1.0f;
    public bool showNotifications = true;
    public bool enableTutorial = true;
    
    [Header("ゲームプレイ設定")]
    public bool autoSave = true;
    public float autoSaveInterval = 60f;
    public bool showCoordinates = false;
    public bool highlightLastMove = true;
    public bool showPossibleMoves = true;
    
    public GameSettings()
    {
        // デフォルト値は上記フィールド初期化で設定済み
    }
    
    public GameSettings(float timeLimit, bool logging = true)
    {
        moveTimeLimit = timeLimit;
        enableLogging = logging;
        enableSoundEffects = true;
        enableMoveAnimation = true;
        difficulty = 1;
        playerName = "Player";
        
        // 音声・グラフィック設定はデフォルト値を使用
        enableAudio = true;
        masterVolume = 1.0f;
        bgmVolume = 0.8f;
        sfxVolume = 1.0f;
        voiceVolume = 1.0f;
        uiVolume = 0.9f;
        
        graphicsQuality = Quality.High;
        targetFrameRate = 60;
        enableVSync = true;
        enableAntiAliasing = true;
        enableShadows = true;
        enableParticles = true;
        
        enableAnimations = true;
        uiScale = 1.0f;
        showNotifications = true;
        enableTutorial = true;
        
        autoSave = true;
        autoSaveInterval = 60f;
        showCoordinates = false;
        highlightLastMove = true;
        showPossibleMoves = true;
    }
    
    /// <summary>
    /// 設定値の妥当性をチェックして修正
    /// </summary>
    public void ValidateAndClamp()
    {
        moveTimeLimit = Mathf.Max(5f, moveTimeLimit);
        masterVolume = Mathf.Clamp01(masterVolume);
        bgmVolume = Mathf.Clamp01(bgmVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
        voiceVolume = Mathf.Clamp01(voiceVolume);
        uiVolume = Mathf.Clamp01(uiVolume);
        targetFrameRate = Mathf.Clamp(targetFrameRate, 30, 120);
        uiScale = Mathf.Clamp(uiScale, 0.5f, 2.0f);
        autoSaveInterval = Mathf.Clamp(autoSaveInterval, 10f, 300f);
        difficulty = Mathf.Clamp(difficulty, 1, 6);
    }
}

/// <summary>
/// ゲーム結果クラス（BattleUIGenerator.cs, UIDialogManager.cs互換）
/// </summary>
[System.Serializable]
public class GameResult
{
    public GameState result;
    public Player winner;
    public string reason;
    public int moveCount;
    public float gameDuration;
    public System.DateTime endTime;
    public List<string> moveHistory;
    
    public GameResult()
    {
        result = GameState.NotStarted;
        winner = Player.Sente;
        reason = "";
        moveCount = 0;
        gameDuration = 0f;
        endTime = System.DateTime.Now;
        moveHistory = new List<string>();
    }
    
    public GameResult(GameState gameState, Player winningPlayer, string gameReason, int moves, float duration)
    {
        result = gameState;
        winner = winningPlayer;
        reason = gameReason;
        moveCount = moves;
        gameDuration = duration;
        endTime = System.DateTime.Now;
        moveHistory = new List<string>();
    }
}

/// <summary>
/// 千日手判定結果クラス（BattleUIGenerator.cs, UIDialogManager.cs互換）
/// </summary>
[System.Serializable]
public class RepetitionResult
{
    public bool isRepetition;
    public int repetitionCount;
    public string repetitionMove;
    public List<string> repeatedMoves;
    public string repetitionType;
    
    public RepetitionResult()
    {
        isRepetition = false;
        repetitionCount = 0;
        repetitionMove = "";
        repeatedMoves = new List<string>();
        repetitionType = "None";
    }
    
    public RepetitionResult(bool repetition, int count, string move, List<string> moves = null)
    {
        isRepetition = repetition;
        repetitionCount = count;
        repetitionMove = move;
        repeatedMoves = moves ?? new List<string>();
        repetitionType = repetition ? "Sennichite" : "None";
    }
}

/// <summary>
/// ゲーム統計情報クラス（GameManager互換）
/// </summary>
[System.Serializable]
public class GameStatistics
{
    public int gamesPlayed;
    public int wins;
    public int losses;
    public int draws;
    public float averageGameTime;
    public int totalMoves;
    public System.DateTime lastPlayTime;
    public string favoriteOpening;
    
    public float GetWinRate()
    {
        if (gamesPlayed == 0) return 0f;
        return (float)wins / gamesPlayed * 100f;
    }
}

// ==========================================================================
// GameLogicクラス - 完全実装版
// ==========================================================================

/// <summary>
/// 将棋ゲーム全体のロジックを統括管理するクラス（既存InputProvider.cs互換）
/// </summary>
public class GameLogic : MonoBehaviour
{
    [Header("コンポーネント参照")]
    public Board board;
    public MoveValidator moveValidator;
    
    [Header("ゲーム設定")]
    public GameSettings gameSettings = new GameSettings();
    public bool enableTimeLimit = true;
    
    [Header("プレイヤー設定")]
    public bool senteIsHuman = true;
    public bool goteIsHuman = false;
    public int aiDifficulty = 1;
    
    // ========== 状態管理 ==========
    private GameState currentGameState = GameState.NotStarted;
    private Dictionary<Player, InputProvider> inputProviders = new Dictionary<Player, InputProvider>();
    private InputProvider currentInputProvider = null;

    // ========== オンライン対戦設定 ==========
    // NetworkGameSyncがStart()より前(SetupInputProviders呼び出し前)に設定する想定
    private bool isOnlineMode = false;
    private Player onlineLocalPlayer = Player.Sente;
    private NetworkInputProvider onlineNetworkProvider = null;

    /// <summary>
    /// オンライン対戦モードを設定する。NetworkGameSyncから呼び出される。
    /// senteIsHuman/goteIsHumanの設定より優先される。
    /// </summary>
    public void ConfigureOnlineMode(Player localPlayerColor, NetworkInputProvider networkProvider)
    {
        isOnlineMode = true;
        onlineLocalPlayer = localPlayerColor;
        onlineNetworkProvider = networkProvider;
        Debug.Log($"🎮 [GameLogic] オンライン対戦モード設定: 自分={localPlayerColor}");
    }
    
    // ========== 時間管理 ==========
    private Dictionary<Player, float> playerTimes = new Dictionary<Player, float>();
    private float gameStartTime;
    private bool isTimerRunning = false;
    
    // ========== 手数・履歴管理 ==========
    private List<Move> moveHistory = new List<Move>();
    private List<string> gameStateHistory = new List<string>();
    private int currentMoveNumber = 1;
    
    // ========== プレイヤー管理 ==========
    private Dictionary<Player, string> playerNames = new Dictionary<Player, string>();
    
    // ========== 千日手判定 ==========
    private Dictionary<string, int> positionCounts = new Dictionary<string, int>();
    private const int REPETITION_LIMIT = 4;
    
    // ========== イベントシステム ==========
    public static event System.Action<GameState> OnGameStateChanged;
    public static event System.Action<Player, float> OnTimeUpdated;
    public static event System.Action<GameResult, string> OnGameEnded;
    public static event System.Action<string> OnGameMessage;
    public static event System.Action<RepetitionResult> OnRepetitionDetected;
    
    // ========== 初期化 ==========
    
    void Awake()
    {
        Debug.Log("🎮 [GameLogic] Awake - Basic initialization");
        
        // 基本参照設定
        if (board == null) board = FindFirstObjectByType<Board>();
        if (moveValidator == null) moveValidator = FindFirstObjectByType<MoveValidator>();
        
        // デフォルト設定
        gameSettings = new GameSettings(30f, true);
    }
    
    void Start()
    {
        Debug.Log("🎮 [GameLogic] Start - Full initialization");
        
        // 遅延初期化で確実にセットアップ
        StartCoroutine(DelayedInitialization());
    }
    
    private IEnumerator DelayedInitialization()
    {
        Debug.Log("🎮 [GameLogic] === Delayed Initialization Start ===");
        
        // コンポーネント初期化完了を待つ
        while (board == null || moveValidator == null)
        {
            yield return new WaitForEndOfFrame();
            if (board == null) board = FindFirstObjectByType<Board>();
            if (moveValidator == null) moveValidator = FindFirstObjectByType<MoveValidator>();
        }
        
        // Boardの初期化完了を待つ
        while (!board.IsInitialized())
        {
            yield return new WaitForEndOfFrame();
        }
        
        Debug.Log("🎮 [GameLogic] Components ready, starting game");
        
        // InputProvider設定
        SetupInputProviders();
        
        // ゲーム開始
        yield return new WaitForEndOfFrame();
        StartNewGame();
        
        Debug.Log("🎮 [GameLogic] === Delayed Initialization Complete ===");
    }
    
    private void SetupInputProviders()
    {
        Debug.Log("🎮 [GameLogic] Setting up input providers");

        if (isOnlineMode && onlineNetworkProvider != null)
        {
            var localHumanProvider = FindFirstObjectByType<HumanInputProvider>();
            if (localHumanProvider == null)
            {
                var providerObj = new GameObject("HumanInputProvider_Local");
                providerObj.transform.SetParent(transform);
                localHumanProvider = providerObj.AddComponent<HumanInputProvider>();
            }

            var remotePlayer = onlineLocalPlayer == Player.Sente ? Player.Gote : Player.Sente;
            inputProviders[onlineLocalPlayer] = localHumanProvider;
            inputProviders[remotePlayer] = onlineNetworkProvider;

            Debug.Log($"🎮 [GameLogic] Input providers (Online) - Local({onlineLocalPlayer}): Human, Remote({remotePlayer}): Network");
            return;
        }

        // 先手InputProvider設定
        if (senteIsHuman)
        {
            var humanProvider = FindFirstObjectByType<HumanInputProvider>();
            if (humanProvider == null)
            {
                var providerObj = new GameObject("HumanInputProvider_Sente");
                providerObj.transform.SetParent(transform);
                humanProvider = providerObj.AddComponent<HumanInputProvider>();
            }
            inputProviders[Player.Sente] = humanProvider;
        }
        else
        {
            var aiProvider = FindFirstObjectByType<AIInputProvider>();
            if (aiProvider == null)
            {
                var providerObj = new GameObject("AIInputProvider_Sente");
                providerObj.transform.SetParent(transform);
                aiProvider = providerObj.AddComponent<AIInputProvider>();
            }
            inputProviders[Player.Sente] = aiProvider;
        }
        
        // 後手InputProvider設定
        if (goteIsHuman)
        {
            var humanProvider = FindFirstObjectByType<HumanInputProvider>();
            if (humanProvider == null || inputProviders[Player.Sente] == humanProvider)
            {
                var providerObj = new GameObject("HumanInputProvider_Gote");
                providerObj.transform.SetParent(transform);
                humanProvider = providerObj.AddComponent<HumanInputProvider>();
            }
            inputProviders[Player.Gote] = humanProvider;
        }
        else
        {
            var aiProvider = FindFirstObjectByType<AIInputProvider>();
            if (aiProvider == null || inputProviders[Player.Sente] == aiProvider)
            {
                var providerObj = new GameObject("AIInputProvider_Gote");
                providerObj.transform.SetParent(transform);
                aiProvider = providerObj.AddComponent<AIInputProvider>();
            }
            inputProviders[Player.Gote] = aiProvider;
        }
        
        Debug.Log($"🎮 [GameLogic] Input providers - Sente: {inputProviders[Player.Sente].GetProviderType()}, Gote: {inputProviders[Player.Gote].GetProviderType()}");
    }
    
    // ========== ゲーム制御（高機能版） ==========
    
    /// <summary>
    /// 新しいゲームを開始（基本版）
    /// </summary>
    public void StartNewGame()
    {
        StartNewGame("先手", "後手", gameSettings);
    }
    
    /// <summary>
    /// 新しいゲームを開始（プレイヤー指定版）
    /// </summary>
    public void StartNewGame(string player1Name, string player2Name)
    {
        StartNewGame(player1Name, player2Name, gameSettings);
    }
    
    /// <summary>
    /// 新しいゲームを開始（完全版）
    /// </summary>
    public void StartNewGame(string player1Name, string player2Name, GameSettings settings)
    {
        Debug.Log($"🎮 [GameLogic] Starting new game: {player1Name} vs {player2Name}");
        
        try
        {
            // 設定適用
            if (settings != null)
            {
                settings.ValidateAndClamp();
                gameSettings = settings;
            }
            
            // ゲーム状態初期化
            currentGameState = GameState.InProgress;
            moveHistory.Clear();
            gameStateHistory.Clear();
            positionCounts.Clear();
            currentMoveNumber = 1;
            
            // プレイヤー名設定
            SetPlayerName(Player.Sente, player1Name);
            SetPlayerName(Player.Gote, player2Name);
            
            // 時間初期化
            float timeLimit = enableTimeLimit ? gameSettings.moveTimeLimit * 60f : float.MaxValue;
            playerTimes[Player.Sente] = timeLimit;
            playerTimes[Player.Gote] = timeLimit;
            gameStartTime = Time.time;
            isTimerRunning = true;
            
            // 盤面初期化（再初期化）
            if (board != null)
            {
                board.InitializeBoard();
                RecordCurrentPosition();
            }
            
            // InputProvider設定
            SetupInputProviders();
            
            // イベント発火
            OnGameStateChanged?.Invoke(currentGameState);
            OnGameMessage?.Invoke($"ゲーム開始: {player1Name} vs {player2Name}");
            
            // 最初の手番開始
            StartPlayerTurn();
            
            Debug.Log($"✅ [GameLogic] Game started successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error starting game: {ex.Message}");
            currentGameState = GameState.Aborted;
            OnGameStateChanged?.Invoke(currentGameState);
        }
    }
    
    /// <summary>
    /// ゲームを一時停止
    /// </summary>
    public void PauseGame()
    {
        if (currentGameState == GameState.InProgress || currentGameState == GameState.Check)
        {
            isTimerRunning = false;
            currentGameState = GameState.Paused;
            OnGameStateChanged?.Invoke(currentGameState);
            OnGameMessage?.Invoke("ゲーム一時停止");
        }
    }
    
    /// <summary>
    /// ゲームを再開
    /// </summary>
    public void ResumeGame()
    {
        if (currentGameState == GameState.Paused)
        {
            isTimerRunning = true;
            currentGameState = GameState.InProgress;
            OnGameStateChanged?.Invoke(currentGameState);
            OnGameMessage?.Invoke("ゲーム再開");
        }
    }
    
    /// <summary>
    /// ゲーム設定を更新
    /// </summary>
    public void UpdateGameSettings(GameSettings newSettings)
    {
        if (newSettings != null)
        {
            newSettings.ValidateAndClamp();
            gameSettings = newSettings;
            
            // 時間制限の更新
            if (enableTimeLimit && isTimerRunning)
            {
                float newTimeLimit = gameSettings.moveTimeLimit * 60f;
                foreach (var player in playerTimes.Keys.ToList())
                {
                    if (playerTimes[player] > newTimeLimit)
                    {
                        playerTimes[player] = newTimeLimit;
                    }
                }
            }
            
            OnGameMessage?.Invoke("ゲーム設定更新");
        }
    }
    
    /// <summary>
    /// プレイヤー名を設定
    /// </summary>
    public void SetPlayerName(Player player, string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            playerNames[player] = name;
        }
    }
    
    /// <summary>
    /// プレイヤー名を取得
    /// </summary>
    public string GetPlayerName(Player player)
    {
        if (playerNames.ContainsKey(player))
        {
            return playerNames[player];
        }
        return player == Player.Sente ? "先手" : "後手";
    }
    
    private void StartPlayerTurn()
    {
        if (!IsGameInProgress()) return;
        
        try
        {
            var currentPlayer = board?.currentPlayer ?? Player.Sente;
            Debug.Log($"🎮 [GameLogic] Starting turn for {currentPlayer}");
            
            // ★修正: 現在のプレイヤーが詰んでいるかチェック（相手の手で詰まされた場合）
            if (CheckForCheckmate(currentPlayer))
            {
                return; // ゲーム終了
            }
            
            // 王手チェック
            if (IsInCheck(currentPlayer))
            {
                currentGameState = GameState.Check;
                OnGameStateChanged?.Invoke(currentGameState);
                OnGameMessage?.Invoke($"{GetPlayerDisplayName(currentPlayer)}が王手です");
            }
            else
            {
                currentGameState = GameState.InProgress;
                OnGameStateChanged?.Invoke(currentGameState);
            }
            
            // InputProviderに手番開始を通知
            if (inputProviders.ContainsKey(currentPlayer))
            {
                currentInputProvider = inputProviders[currentPlayer];
                currentInputProvider.OnTurnStart(currentPlayer);
                
                // InputProviderから手を取得（ポーリング）
                StartCoroutine(PollForMove(currentPlayer));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error starting player turn: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 詰みチェック - ゲーム終了処理を含む
    /// </summary>
    private bool CheckForCheckmate(Player player)
    {
        if (moveValidator == null || board == null) return false;
        
        // 王手かつ合法手がない = 詰み
        if (moveValidator.IsInCheck(player))
        {
            var legalMoves = moveValidator.GetAllLegalMoves(player);
            
            Debug.Log($"🎮 [CheckForCheckmate] {player}: 王手中, 合法手数={legalMoves.Count}");
            
            if (legalMoves.Count == 0)
            {
                // 詰み！
                var winner = (player == Player.Sente) ? Player.Gote : Player.Sente;
                
                Debug.Log($"🎮 [CheckForCheckmate] {player}の詰み! 勝者: {winner}");
                
                currentGameState = GameState.Checkmate;
                OnGameStateChanged?.Invoke(currentGameState);
                
                EndGame(
                    winner == Player.Sente ? GameState.SenteWin : GameState.GoteWin, 
                    winner, 
                    "詰み"
                );
                
                return true;
            }
        }
        
        return false;
    }
    
    // InputProviderから手を定期的に取得するコルーチン
    private IEnumerator PollForMove(Player player)
    {
        while (IsGameInProgress() && board.currentPlayer == player)
        {
            if (currentInputProvider != null)
            {
                var move = currentInputProvider.GetNextMove(player, board, moveValidator);
                if (move != null)
                {
                    Debug.Log($"🎮 [GameLogic] Move received from {currentInputProvider.GetProviderType()}: {move.notation}");
                    
                    if (ExecuteMove(move))
                    {
                        OnMoveCompleted(move);
                        yield break; // 手が成功したらポーリング終了
                    }
                }
            }
            
            yield return new WaitForSeconds(0.1f); // 100ms間隔でポーリング
        }
    }
    
    // ========== 手の処理 ==========
    
    public void ProcessHumanMove(Move move)
    {
        if (!IsGameInProgress()) return;
        if (!CanAcceptHumanInput()) return;
        
        Debug.Log($"🎮 [GameLogic] Processing human move: {move?.notation}");
        
        try
        {
            if (ExecuteMove(move))
            {
                OnMoveCompleted(move);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error processing human move: {ex.Message}");
        }
    }
    
    private bool ExecuteMove(Move move)
    {
        if (move == null || board == null || moveValidator == null) return false;
        
        // 最終妥当性チェック
        if (!moveValidator.IsValidMove(move)) return false;
        
        // 手番チェック
        if (move.player != board.currentPlayer) return false;
        
        Debug.Log($"🎮 [GameLogic] Executing move: {move.notation}");
        
        // 手を実行（BoardExtensionsのExecuteMoveを使用）
        bool success = board.ExecuteMove(move);
        if (success)
        {
            // 履歴に追加
            moveHistory.Add(move);
            currentMoveNumber++;
            
            // 千日手チェック
            RecordCurrentPosition();
            CheckForRepetition();
        }
        
        return success;
    }
    
    private void OnMoveCompleted(Move move)
    {
        try
        {
            // 現在のInputProviderの手番終了
            var previousPlayer = (board.currentPlayer == Player.Sente) ? Player.Gote : Player.Sente;
            currentInputProvider?.OnTurnEnd(previousPlayer);
            
            // ★修正: 手が完了した直後に相手（次の手番のプレイヤー）の詰みをチェック
            // board.currentPlayerは既に切り替わっている
            Debug.Log($"🎮 [OnMoveCompleted] 手完了後、次の手番: {board.currentPlayer}");
            
            // 手番切り替え後の処理を少し遅延
            StartCoroutine(DelayedTurnSwitch());
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error in move completion: {ex.Message}");
        }
    }
    
    private IEnumerator DelayedTurnSwitch()
    {
        yield return new WaitForEndOfFrame();
        
        // ゲーム終了チェック
        if (!IsGameInProgress()) yield break;
        
        // 次の手番開始（この中で詰みチェックも行われる）
        StartPlayerTurn();
    }
    
    // ========== 状態判定 ==========
    
    public bool IsGameInProgress()
    {
        return currentGameState == GameState.InProgress || currentGameState == GameState.Check;
    }
    
    public bool CanAcceptHumanInput()
    {
        if (!IsGameInProgress()) return false;
        if (currentInputProvider == null) return false;
        return currentInputProvider is HumanInputProvider;
    }
    
    public bool IsAIPlayerTurn()
    {
        if (currentInputProvider == null) return false;
        return currentInputProvider is AIInputProvider;
    }
    
    private bool IsInCheck(Player player)
    {
        try
        {
            if (board == null || moveValidator == null) return false;
            return moveValidator.IsInCheck(player);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
    
    private bool IsInCheckmate(Player player)
    {
        try
        {
            if (board == null || moveValidator == null) return false;
            return moveValidator.IsCheckmate(player);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
    
    // ========== 千日手判定 ==========
    
    private void RecordCurrentPosition()
    {
        try
        {
            if (board == null) return;
            
            string positionKey = GeneratePositionKey();
            
            if (positionCounts.ContainsKey(positionKey))
            {
                positionCounts[positionKey]++;
            }
            else
            {
                positionCounts[positionKey] = 1;
            }
        }
        catch (System.Exception)
        {
            // エラーを無視
        }
    }
    
    private void CheckForRepetition()
    {
        try
        {
            if (board == null) return;
            
            string currentPosition = GeneratePositionKey();
            
            if (positionCounts.ContainsKey(currentPosition) && positionCounts[currentPosition] >= REPETITION_LIMIT)
            {
                var repetitionResult = new RepetitionResult(true, positionCounts[currentPosition], currentPosition);
                OnRepetitionDetected?.Invoke(repetitionResult);
                
                // 千日手で引き分け
                EndGame(GameState.Draw, Player.Sente, "千日手");
            }
        }
        catch (System.Exception)
        {
            // エラーを無視
        }
    }
    
    private string GeneratePositionKey()
    {
        try
        {
            if (board == null) return "";
            
            var key = new System.Text.StringBuilder();
            
            // 盤面の駒配置
            for (int file = 1; file <= 9; file++)
            {
                for (int rank = 1; rank <= 9; rank++)
                {
                    var piece = board.GetPiece(new Position(file, rank));
                    if (piece != null)
                    {
                        key.Append($"{file}{rank}{(int)piece.type}{(int)piece.owner}");
                    }
                    else
                    {
                        key.Append("0");
                    }
                }
            }
            
            // 手番
            key.Append($"_{(int)board.currentPlayer}");
            
            return key.ToString();
        }
        catch (System.Exception)
        {
            return System.Guid.NewGuid().ToString();
        }
    }
    
    // ========== 時間管理 ==========
    
    void Update()
    {
        if (isTimerRunning && enableTimeLimit && IsGameInProgress())
        {
            UpdateTimer();
        }
    }
    
    private void UpdateTimer()
    {
        try
        {
            if (board == null) return;
            
            var currentPlayer = board.currentPlayer;
            
            if (playerTimes.ContainsKey(currentPlayer))
            {
                playerTimes[currentPlayer] -= Time.deltaTime;
                
                // 時間切れチェック
                if (playerTimes[currentPlayer] <= 0f)
                {
                    playerTimes[currentPlayer] = 0f;
                    var winner = currentPlayer == Player.Sente ? Player.Gote : Player.Sente;
                    EndGame(winner == Player.Sente ? GameState.SenteWin : GameState.GoteWin, winner, "時間切れ");
                    return;
                }
                
                // 時間更新イベント
                OnTimeUpdated?.Invoke(currentPlayer, playerTimes[currentPlayer]);
            }
        }
        catch (System.Exception)
        {
            // エラーを無視
        }
    }
    
    public float GetTimeRemaining(Player player)
    {
        return playerTimes.ContainsKey(player) ? playerTimes[player] : 0f;
    }
    
    // ========== ゲーム終了 ==========
    
    public void EndGame(GameState endState, Player winner, string reason)
    {
        Debug.Log($"🎮 [GameLogic] Game ended: {endState} - {reason}");
        
        try
        {
            currentGameState = endState;
            isTimerRunning = false;
            
            // InputProvider終了処理
            currentInputProvider?.OnTurnEnd(board.currentPlayer);
            
            // ゲーム結果作成
            var gameResult = new GameResult(
                endState,
                winner,
                reason,
                currentMoveNumber - 1,
                Time.time - gameStartTime
            );
            
            // イベント発火
            OnGameEnded?.Invoke(gameResult, reason);
            OnGameStateChanged?.Invoke(currentGameState);
            OnGameMessage?.Invoke($"ゲーム終了: {reason}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error ending game: {ex.Message}");
        }
    }
    
    // ========== ユーティリティメソッド ==========
    
    public GameState GetCurrentGameState()
    {
        return currentGameState;
    }
    
    public List<Move> GetMoveHistory()
    {
        return new List<Move>(moveHistory);
    }
    
    public int GetCurrentMoveNumber()
    {
        return currentMoveNumber;
    }
    
    public string GetPlayerDisplayName(Player player)
    {
        if (playerNames.ContainsKey(player) && !string.IsNullOrEmpty(playerNames[player]))
        {
            return playerNames[player];
        }
        return player == Player.Sente ? "先手" : "後手";
    }
    
    /// <summary>
    /// 投了ボタンが押された時の処理
    /// </summary>
    public void OnResignClicked()
    {
        try
        {
            if (!IsGameInProgress()) return;
            
            // 現在のプレイヤーの負け
            var winner = board.currentPlayer == Player.Sente ? Player.Gote : Player.Sente;
            var loser = board.currentPlayer;
            
            EndGame(
                winner == Player.Sente ? GameState.SenteWin : GameState.GoteWin,
                winner,
                $"{GetPlayerDisplayName(loser)}が投了"
            );
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🎮 [GameLogic] Error handling resign: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 現在のInputProviderを取得
    /// </summary>
    public InputProvider GetCurrentInputProvider()
    {
        return currentInputProvider;
    }
}