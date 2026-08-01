using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 03_GamePlayに全部詰め込まれているログイン/メニュー画面と、常駐マネージャー群を
/// 01_MainMenuへ分離するための一回限りのエディタ支援ツール。
///
/// 使い方:
/// 1. Unityメニュー "ShogiArena > Scene Split > 1. Report Hierarchy (dry run)" を実行し、
///    Consoleに出力される内容をそのままClaudeに貼り付けて、想定通りか確認してから2に進む。
/// 2. 問題なければ "ShogiArena > Scene Split > 2. Split MainMenu from GamePlay" を実行。
///    実行前に必ずシーンの変更を保存またはコミットしておくこと(Ctrl+Zは効くが保存後は戻せない)。
/// </summary>
public static class ShogiSceneSplitter
{
    private const string GamePlayScenePath = "Assets/Scenes/03_GamePlay.unity";
    private const string MainMenuScenePath = "Assets/Scenes/01_MainMenu.unity";

    // 01_MainMenuへ移す対象(名前で検索し、見つかったオブジェクトのtransform.rootを移動する。
    // ネストの深さに依存しないようにしている)。
    // - ログイン/登録/メニューのUI画面
    // - 常駐マネージャー群(Singleton<T>継承。どのシーンにあっても動くが、
    //   Inspectorで設定済みの値(ShogiSceneManagerのsceneDatabase等)を保持するには
    //   最初にロードされるシーン=01_MainMenu側に置く必要がある)
    private static readonly string[] TargetObjectNames =
    {
        // UI
        "MenuUIScreen", "LoginScreen", "RegisterScreen",
        // Managers
        "UIManager", "GameManager", "AuthManager", "ConfigManager",
        "EnhancedAuthManager", "FirebaseDataManager", "FirebaseManager",
        "BasicPhotonManager", "AudioManager", "SceneManager",
        "EventSystem", "Main Camera", "PhotonMono",
    };

    [MenuItem("ShogiArena/Scene Split/1. Report Hierarchy (dry run)")]
    public static void ReportHierarchy()
    {
        var scene = EditorSceneManager.OpenScene(GamePlayScenePath, OpenSceneMode.Single);

        Debug.Log("=== [ShogiSceneSplitter] 03_GamePlay ルートオブジェクト一覧 ===");
        foreach (var root in scene.GetRootGameObjects())
        {
            Debug.Log($"ROOT: {root.name}");
        }

        Debug.Log("=== [ShogiSceneSplitter] 移動対象オブジェクト -> ルート祖先 ===");
        foreach (var name in TargetObjectNames)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                Debug.LogWarning($"見つかりません: {name}");
                continue;
            }
            var root = go.transform.root.gameObject;
            Debug.Log($"{name} -> root: {root.name}{(root == go ? " (自身がルート)" : "")}");
        }

        Debug.Log("=== [ShogiSceneSplitter] 上記を確認したうえで、問題なければ手順2を実行してください ===");
    }

    [MenuItem("ShogiArena/Scene Split/2. Split MainMenu from GamePlay")]
    public static void SplitMainMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "シーン分割の実行確認",
                "03_GamePlayからログイン/メニューUIと常駐マネージャーを01_MainMenuへ移動します。\n" +
                "事前にシーンの変更を保存・コミット済みですか?(この操作は元に戻しにくいです)",
                "実行する", "キャンセル"))
        {
            Debug.Log("[ShogiSceneSplitter] キャンセルしました。");
            return;
        }

        var gamePlayScene = EditorSceneManager.OpenScene(GamePlayScenePath, OpenSceneMode.Single);
        var mainMenuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneSetupMode.Additive);

        var movedRoots = new HashSet<GameObject>();
        var notFound = new List<string>();

        foreach (var name in TargetObjectNames)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                notFound.Add(name);
                continue;
            }
            var root = go.transform.root.gameObject;
            if (movedRoots.Contains(root)) continue;

            EditorSceneManager.MoveGameObjectToScene(root, mainMenuScene);
            movedRoots.Add(root);
            Debug.Log($"[ShogiSceneSplitter] 移動: '{root.name}' (対象: {name})");
        }

        if (notFound.Count > 0)
        {
            Debug.LogWarning($"[ShogiSceneSplitter] 見つからなかったオブジェクト(スキップ): {string.Join(", ", notFound)}");
        }

        if (movedRoots.Count == 0)
        {
            Debug.LogError("[ShogiSceneSplitter] 何も移動されませんでした。処理を中断します(シーンは保存しません)。");
            return;
        }

        EditorSceneManager.SaveScene(mainMenuScene, MainMenuScenePath);
        EditorSceneManager.SaveScene(gamePlayScene, GamePlayScenePath);

        Debug.Log($"[ShogiSceneSplitter] 完了。{movedRoots.Count}件のルートオブジェクトを01_MainMenu.unityへ移動し、" +
                  "両シーンを保存しました。Unity上で両シーンを開いて内容を確認してください。" +
                  "また、ShogiSceneManagerのsceneDatabase(Inspector)で各SceneTypeにsceneNameが" +
                  "正しく設定されているか(MainMenu=\"01_MainMenu\", Game=\"03_GamePlay\"等)を確認し、" +
                  "Build Settingsに01_MainMenu, 02_ModeSelect, 03_GamePlay, 04_Settingsを" +
                  "この順で登録してください(01_MainMenuが最初=起動シーン)。");
    }
}
