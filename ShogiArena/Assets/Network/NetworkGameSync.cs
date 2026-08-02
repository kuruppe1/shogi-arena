using UnityEngine;
using Photon.Pun;

/// <summary>
/// オンライン対戦時に、ローカルで指した手をPhoton経由で相手へ送信し、
/// 相手が指した手をNetworkInputProviderへ渡す橋渡し役。
///
/// 前提: このGameObjectにはPhotonViewコンポーネントが必要(FriendBattleManager等と同様のパターン)。
/// Photonルームに参加している場合のみ動作する(InRoomでなければ何もせず無効化され、
/// 既存のローカル/AI対戦には影響しない)。
///
/// 役割の割り当て規約: マスタークライアント=先手、参加者=後手。
/// (注意: 未検証。Unity Editorでの動作確認が必要)
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkGameSync : MonoBehaviourPunCallbacks
{
    [Header("Debug")]
    public bool showDebugInfo = true;

    private GameLogic gameLogic;
    private NetworkInputProvider networkInputProvider;
    private SpectatorModeManager spectatorModeManager;
    private Player localPlayerColor;
    private bool isActive = false;
    private int moveNumber = 0;

    void Start()
    {
        if (!PhotonNetwork.InRoom)
        {
            if (showDebugInfo)
            {
                Debug.Log("[NetworkGameSync] Photonルーム未参加のため無効化(ローカル/AI対戦として動作)");
            }
            enabled = false;
            return;
        }

        spectatorModeManager = FindFirstObjectByType<SpectatorModeManager>();
        if (spectatorModeManager != null && spectatorModeManager.IsSpectating())
        {
            if (showDebugInfo)
            {
                Debug.Log("[NetworkGameSync] 観戦者のため無効化(対局への入力は行わない)");
            }
            enabled = false;
            return;
        }

        gameLogic = FindFirstObjectByType<GameLogic>();
        if (gameLogic == null)
        {
            Debug.LogError("[NetworkGameSync] GameLogicが見つかりません");
            enabled = false;
            return;
        }

        // マスタークライアント=先手、参加者=後手
        localPlayerColor = PhotonNetwork.IsMasterClient ? Player.Sente : Player.Gote;

        var providerObj = new GameObject("NetworkInputProvider");
        providerObj.transform.SetParent(transform);
        networkInputProvider = providerObj.AddComponent<NetworkInputProvider>();
        networkInputProvider.isConnected = true;

        gameLogic.ConfigureOnlineMode(localPlayerColor, networkInputProvider);
        isActive = true;

        Board.OnMoveExecuted += HandleLocalMoveExecuted;

        if (showDebugInfo)
        {
            Debug.Log($"[NetworkGameSync] オンライン対戦を有効化。自分の手番: {localPlayerColor}");
        }
    }

    void OnDestroy()
    {
        Board.OnMoveExecuted -= HandleLocalMoveExecuted;
    }

    /// <summary>
    /// Board.OnMoveExecutedは自分側・相手側どちらの手が指されても発火するため、
    /// 自分の駒の手だけを送信対象にする(相手から受信して盤面に反映した手を送り返さないため)。
    /// </summary>
    private void HandleLocalMoveExecuted(Move move)
    {
        if (!isActive || move == null) return;
        if (move.player != localPlayerColor) return;

        photonView.RPC(nameof(ReceiveMoveRPC), RpcTarget.Others,
            move.from.file, move.from.rank, move.to.file, move.to.rank,
            (int)move.pieceType, (int)move.player, move.isPromote, move.isDrop);

        // 観戦者への通知(自分が指した手のみ。相手から受信した手はReceiveMoveRPC側で二重送信しない)
        moveNumber++;
        if (spectatorModeManager != null)
        {
            spectatorModeManager.UpdateGameMove(move.notation, move.player.ToString(), moveNumber);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[NetworkGameSync] 手を送信: {move.notation}");
        }
    }

    [PunRPC]
    private void ReceiveMoveRPC(int fromFile, int fromRank, int toFile, int toRank,
        int pieceTypeInt, int playerInt, bool isPromote, bool isDrop)
    {
        var player = (Player)playerInt;
        var pieceType = (PieceType)pieceTypeInt;
        var toPos = new Position(toFile, toRank);

        Move move = isDrop
            ? new Move(toPos, pieceType, player)
            : new Move(new Position(fromFile, fromRank), toPos, pieceType, player, isPromote);

        if (networkInputProvider != null)
        {
            networkInputProvider.SetNetworkMove(move);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[NetworkGameSync] 相手の手を受信: {move.notation}");
        }
    }
}
