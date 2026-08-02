using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;

/// <summary>
/// フレンド対戦システム
/// プライベートルーム作成・参加、カスタム設定機能
/// </summary>
public class FriendBattleManager : MonoBehaviourPunCallbacks
{
    [Header("プライベートルーム設定")]
    public int roomCodeLength = 6;                 // ルームコード長
    public bool showUI = false;                    // フレンド対戦UI表示
    
    [Header("デバッグ設定")]
    public bool showDebugInfo = true;
    
    // フレンド対戦用変数
    private string roomCodeInput = "";
    private string currentPrivateRoomCode = "";
    private bool isPrivateRoomHost = false;
    
    // イベント
    public System.Action<string> OnPrivateRoomCreated;       // プライベートルーム作成
    public System.Action<string> OnPrivateRoomJoined;       // プライベートルーム参加
    public System.Action OnPrivateRoomLeft;                 // プライベートルーム退出
    
    void Start()
    {
        if (showDebugInfo)
        {
            Debug.Log("[FriendBattleManager] フレンド対戦システム初期化完了");
        }
    }
    
    /// <summary>
    /// プライベートルーム作成
    /// </summary>
    public void CreatePrivateRoom()
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning("[FriendBattleManager] Photonに接続されていません");
            return;
        }
        
        // ルームコード生成
        string roomCode = GenerateRoomCode();
        string roomName = "Private_" + roomCode;
        
        // ルーム設定
        RoomOptions roomOptions = new RoomOptions()
        {
            MaxPlayers = 6,  // 対戦者2人 + 観戦者4人
            IsVisible = false,  // プライベートルームは非表示
            IsOpen = true
        };
        
        // カスタムプロパティ設定
        var customProps = new ExitGames.Client.Photon.Hashtable();
        customProps["roomCode"] = roomCode;
        customProps["hostId"] = PhotonNetwork.LocalPlayer.UserId;
        customProps["createdTime"] = System.DateTime.Now.ToBinary();
        customProps["allowSpectators"] = true;
        
        roomOptions.CustomRoomProperties = customProps;
        
        // ルーム作成
        bool result = PhotonNetwork.CreateRoom(roomName, roomOptions);
        
        if (result)
        {
            currentPrivateRoomCode = roomCode;
            isPrivateRoomHost = true;
            
            if (showDebugInfo)
            {
                Debug.Log($"[FriendBattleManager] プライベートルーム作成: {roomCode}");
            }
            
            OnPrivateRoomCreated?.Invoke(roomCode);
        }
        else
        {
            Debug.LogWarning("[FriendBattleManager] プライベートルーム作成に失敗しました");
        }
    }
    
    /// <summary>
    /// プライベートルーム参加
    /// </summary>
    public void JoinPrivateRoom(string roomCode)
    {
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning("[FriendBattleManager] Photonに接続されていません");
            return;
        }
        
        if (string.IsNullOrEmpty(roomCode))
        {
            Debug.LogWarning("[FriendBattleManager] ルームコードが空です");
            return;
        }
        
        string roomName = "Private_" + roomCode.ToUpper();
        
        // ルーム参加
        bool result = PhotonNetwork.JoinRoom(roomName);
        
        if (result)
        {
            currentPrivateRoomCode = roomCode.ToUpper();
            isPrivateRoomHost = false;
            
            if (showDebugInfo)
            {
                Debug.Log($"[FriendBattleManager] プライベートルーム参加試行: {roomCode}");
            }
        }
        else
        {
            Debug.LogWarning($"[FriendBattleManager] プライベートルーム参加に失敗: {roomCode}");
        }
    }
    
    /// <summary>
    /// ルームコード生成
    /// </summary>
    string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string result = "";
        
        for (int i = 0; i < roomCodeLength; i++)
        {
            result += chars[UnityEngine.Random.Range(0, chars.Length)];
        }
        
        return result;
    }
    
    /// <summary>
    /// プライベートルーム退出
    /// </summary>
    public void LeavePrivateRoom()
    {
        if (PhotonNetwork.InRoom && IsInPrivateRoom())
        {
            PhotonNetwork.LeaveRoom();
        }
        
        ResetPrivateRoomData();
        OnPrivateRoomLeft?.Invoke();
        
        if (showDebugInfo)
        {
            Debug.Log("[FriendBattleManager] プライベートルームから退出");
        }
    }
    
    /// <summary>
    /// プライベートルームデータリセット
    /// </summary>
    void ResetPrivateRoomData()
    {
        currentPrivateRoomCode = "";
        isPrivateRoomHost = false;
    }
    
    /// <summary>
    /// プライベートルームにいるかチェック
    /// </summary>
    public bool IsInPrivateRoom()
    {
        return PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.Name.StartsWith("Private_");
    }
    
    /// <summary>
    /// プライベートルームのホストかチェック
    /// </summary>
    public bool IsPrivateRoomHost()
    {
        return isPrivateRoomHost && IsInPrivateRoom();
    }
    
    /// <summary>
    /// 現在のルームコード取得
    /// </summary>
    public string GetCurrentRoomCode()
    {
        return currentPrivateRoomCode;
    }
    
    /// <summary>
    /// プライベートルーム情報取得
    /// </summary>
    public string GetPrivateRoomInfo()
    {
        if (!IsInPrivateRoom()) return "プライベートルームに参加していません";
        
        int playerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        string hostInfo = isPrivateRoomHost ? " (ホスト)" : "";
        
        return $"プライベートルーム: {currentPrivateRoomCode} | プレイヤー: {playerCount}/6{hostInfo}";
    }
    
    /// <summary>
    /// 対戦開始可能かチェック
    /// </summary>
    public bool CanStartGame()
    {
        return IsPrivateRoomHost() && PhotonNetwork.CurrentRoom.PlayerCount >= 2;
    }
    
    /// <summary>
    /// ゲーム開始
    /// </summary>
    public void StartGame()
    {
        if (!CanStartGame())
        {
            Debug.LogWarning("[FriendBattleManager] ゲーム開始条件を満たしていません");
            return;
        }
        
        // RPCでゲーム開始を通知
        photonView.RPC("OnGameStartRPC", RpcTarget.All);
        
        if (showDebugInfo)
        {
            Debug.Log("[FriendBattleManager] プライベートルームでゲーム開始");
        }
    }
    
    /// <summary>
    /// ゲーム開始通知（RPC）
    /// </summary>
    [PunRPC]
    void OnGameStartRPC()
    {
        if (showDebugInfo)
        {
            Debug.Log("[FriendBattleManager] ゲーム開始通知受信");
        }

        // ゲームシーンへ遷移。NetworkGameSyncがシーン内でPhotonルームの状態を検出し、
        // オンライン対戦モードの設定(先手/後手の割り当て・指し手の同期)を行う。
        var sceneManager = ShogiSceneManager.Instance;
        if (sceneManager != null)
        {
            sceneManager.LoadGameScene(GameMode.Online);
        }
        else
        {
            Debug.LogError("[FriendBattleManager] ShogiSceneManagerが見つからないためゲームシーンへ遷移できません");
        }
    }
    
    /// <summary>
    /// Photonルームイベント: 参加時
    /// </summary>
    public override void OnJoinedRoom()
    {
        if (IsInPrivateRoom())
        {
            // プライベートルームのカスタムプロパティから情報取得
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("roomCode", out object roomCodeObj))
            {
                currentPrivateRoomCode = roomCodeObj.ToString();
            }
            
            OnPrivateRoomJoined?.Invoke(currentPrivateRoomCode);
            
            if (showDebugInfo)
            {
                Debug.Log($"[FriendBattleManager] プライベートルーム参加完了: {currentPrivateRoomCode}");
            }
        }
    }
    
    /// <summary>
    /// Photonルームイベント: 退出時
    /// </summary>
    public override void OnLeftRoom()
    {
        ResetPrivateRoomData();
        OnPrivateRoomLeft?.Invoke();
        
        if (showDebugInfo)
        {
            Debug.Log("[FriendBattleManager] ルーム退出完了");
        }
    }
    
    /// <summary>
    /// Photonルームイベント: プレイヤー参加
    /// </summary>
    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (showDebugInfo)
        {
            Debug.Log($"[FriendBattleManager] プレイヤー参加: {newPlayer.NickName}");
        }
    }
    
    /// <summary>
    /// Photonルームイベント: プレイヤー退出
    /// </summary>
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        if (showDebugInfo)
        {
            Debug.Log($"[FriendBattleManager] プレイヤー退出: {otherPlayer.NickName}");
        }
    }
    
    /// <summary>
    /// GUI表示
    /// </summary>
    void OnGUI()
    {
        if (!showUI) return;
        
        DrawFriendBattleUI();
    }
    
    /// <summary>
    /// フレンド対戦UI描画
    /// </summary>
    void DrawFriendBattleUI()
    {
        GUI.Box(new Rect(430, 180, 400, 200), "フレンド対戦");
        
        if (!PhotonNetwork.InRoom)
        {
            // ルーム作成
            if (GUI.Button(new Rect(440, 210, 120, 30), "ルーム作成"))
            {
                CreatePrivateRoom();
            }
            
            if (!string.IsNullOrEmpty(currentPrivateRoomCode))
            {
                GUI.Label(new Rect(440, 250, 300, 25), $"作成したルームコード: {currentPrivateRoomCode}");
            }
            
            // ルーム参加
            GUI.Label(new Rect(440, 280, 100, 25), "ルームコード:");
            roomCodeInput = GUI.TextField(new Rect(540, 280, 100, 25), roomCodeInput);
            
            if (GUI.Button(new Rect(650, 280, 80, 25), "参加"))
            {
                JoinPrivateRoom(roomCodeInput);
            }
            
            GUI.Label(new Rect(440, 310, 350, 25), "※フレンド対戦は対戦回数制限なし");
        }
        else if (IsInPrivateRoom())
        {
            // プライベートルーム内UI
            GUI.Label(new Rect(440, 210, 300, 25), GetPrivateRoomInfo());
            
            GUI.Label(new Rect(440, 235, 100, 25), "プレイヤー:");
            int yPos = 255;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                string playerLabel = player.NickName;
                if (player.IsLocal) playerLabel += " (あなた)";
                if (isPrivateRoomHost && player.IsLocal) playerLabel += " (ホスト)";
                
                GUI.Label(new Rect(450, yPos, 200, 20), playerLabel);
                yPos += 20;
            }
            
            if (GUI.Button(new Rect(440, 320, 100, 30), "ルーム退出"))
            {
                LeavePrivateRoom();
                showUI = false;
            }
            
            if (CanStartGame())
            {
                if (GUI.Button(new Rect(550, 320, 120, 30), "ゲーム開始"))
                {
                    StartGame();
                }
            }
            else if (PhotonNetwork.CurrentRoom.PlayerCount < 2)
            {
                GUI.Label(new Rect(440, 295, 300, 25), "フレンドの参加を待っています...");
            }
        }
        else
        {
            GUI.Label(new Rect(440, 210, 300, 25), "通常ルームに参加中");
            GUI.Label(new Rect(440, 235, 300, 25), "フレンド対戦を使用するには一度退出してください");
        }
    }
}