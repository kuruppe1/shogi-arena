using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 03_GamePlayに全部詰め込まれているログイン/メニュー画面と、常駐マネージャー群を
/// 01_MainMenuへ分離するための一回限りのエディタ支援ツール。
///
/// scene_hierarchy_report.txt(手順1の出力)で判明した通り、既存の親グループ
/// (Game Managers / UI System / System)は「常駐すべき物」と「対局専用で
/// 03_GamePlayに残すべき物」が混在しているため、グループ丸ごとではなく
/// オブジェクトを個別に親から切り離して移動する方式にしている。
///
/// 使い方:
/// 1. "ShogiArena > Scene Split > 1. Report Hierarchy (dry run)" を実行し、
///    出力される scene_hierarchy_report.txt の内容を確認する。
/// 2. 問題なければ "ShogiArena > Scene Split > 2. Split MainMenu from GamePlay" を実行。
///    実行前に必ずシーンの変更を保存またはコミットしておくこと(この操作は元に戻しにくい)。
/// </summary>
public static class ShogiSceneSplitter
{
    private const string GamePlayScenePath = "Assets/Scenes/03_GamePlay.unity";
    private const string MainMenuScenePath = "Assets/Scenes/01_MainMenu.unity";

    // 個別に親から切り離して移動する対象。
    // "UI Documents" は LoginScreen/RegisterScreen/MenuUIScreen の親なのでこれごと移動する。
    // "Game Managers"配下のGameLogic/Board/MoveValidator/BattleUIManager、
    // "UI System"配下のGameUICanvas、"System"配下のYaneuraOuAIは対局専用のため対象に含めない。
    private static readonly string[] IndividualMoveTargets =
    {
        "UI Documents",
        "UIManager",
        "GameManager",
        "ConfigManager",
        "AudioManager",
        "SceneManager",
        "EventSystem",
    };

    // ルートごと移動して問題ない対象(中身が全て常駐マネージャーのみと確認済み)。
    // "BasicPhotonManager"は非アクティブのためGameObject.Findで個別検索できないので、
    // 親の"Network & Auth"ごと移動することでこれも一緒に移す。
    private static readonly string[] WholeRootMoveTargets =
    {
        "Network & Auth",
    };

    private static readonly string ReportFilePath =
        Path.Combine(Application.dataPath, "..", "scene_hierarchy_report.txt");

    [MenuItem("ShogiArena/Scene Split/1. Report Hierarchy (dry run)")]
    public static void ReportHierarchy()
    {
        var scene = EditorSceneManager.OpenScene(GamePlayScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();

        sb.AppendLine("=== 03_GamePlay 全階層(ルートから全ての子孫まで) ===");
        foreach (var root in scene.GetRootGameObjects())
        {
            AppendHierarchy(sb, root.transform, 0);
        }

        sb.AppendLine();
        sb.AppendLine("=== 個別移動対象 -> 親 ===");
        foreach (var name in IndividualMoveTargets)
        {
            var go = GameObject.Find(name);
            sb.AppendLine(go == null
                ? $"見つかりません: {name}"
                : $"{name} -> parent: {(go.transform.parent != null ? go.transform.parent.name : "(ルート)")}");
        }

        sb.AppendLine();
        sb.AppendLine("=== グループ丸ごと移動対象 ===");
        foreach (var name in WholeRootMoveTargets)
        {
            var go = GameObject.Find(name);
            sb.AppendLine(go == null ? $"見つかりません: {name}" : $"{name}: OK");
        }

        File.WriteAllText(ReportFilePath, sb.ToString(), Encoding.UTF8);
        var fullPath = Path.GetFullPath(ReportFilePath);

        Debug.Log($"[ShogiSceneSplitter] レポートを書き出しました: {fullPath}");
    }

    private static void AppendHierarchy(StringBuilder sb, Transform t, int depth)
    {
        sb.AppendLine(new string('-', depth * 2) + " " + t.name);
        for (int i = 0; i < t.childCount; i++)
        {
            AppendHierarchy(sb, t.GetChild(i), depth + 1);
        }
    }

    [MenuItem("ShogiArena/Scene Split/2. Split MainMenu from GamePlay")]
    public static void SplitMainMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "シーン分割の実行確認",
                "03_GamePlayからログイン/メニューUIと常駐マネージャーを個別に01_MainMenuへ移動します。\n" +
                "対局専用オブジェクト(Board/GameLogic/GameUICanvas/YaneuraOuAI等)はそのまま残ります。\n" +
                "事前にシーンの変更を保存・コミット済みですか?(この操作は元に戻しにくいです)",
                "実行する", "キャンセル"))
        {
            Debug.Log("[ShogiSceneSplitter] キャンセルしました。");
            return;
        }

        var gamePlayScene = EditorSceneManager.OpenScene(GamePlayScenePath, OpenSceneMode.Single);
        var mainMenuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        int movedCount = 0;
        var notFound = new List<string>();

        foreach (var name in IndividualMoveTargets)
        {
            var go = GameObject.Find(name);
            if (go == null) { notFound.Add(name); continue; }

            go.transform.SetParent(null, true); // 親から切り離してこのシーンのルートにする
            EditorSceneManager.MoveGameObjectToScene(go, mainMenuScene);
            movedCount++;
            Debug.Log($"[ShogiSceneSplitter] 移動(個別): '{name}'");
        }

        foreach (var name in WholeRootMoveTargets)
        {
            var go = GameObject.Find(name);
            if (go == null) { notFound.Add(name); continue; }

            var root = go.transform.root.gameObject;
            EditorSceneManager.MoveGameObjectToScene(root, mainMenuScene);
            movedCount++;
            Debug.Log($"[ShogiSceneSplitter] 移動(グループ丸ごと): '{root.name}'");
        }

        if (notFound.Count > 0)
        {
            Debug.LogWarning($"[ShogiSceneSplitter] 見つからなかったオブジェクト(スキップ): {string.Join(", ", notFound)}");
        }

        if (movedCount == 0)
        {
            Debug.LogError("[ShogiSceneSplitter] 何も移動されませんでした。処理を中断します(シーンは保存しません)。");
            return;
        }

        // 01_MainMenuには専用のカメラ・ライトがまだ無いため、最低限のものを用意しておく
        // (本格的なUI/UXデザインは次のフェーズ)
        var cameraObj = new GameObject("Main Camera");
        cameraObj.AddComponent<Camera>();
        cameraObj.AddComponent<AudioListener>();
        cameraObj.tag = "MainCamera";
        EditorSceneManager.MoveGameObjectToScene(cameraObj, mainMenuScene);

        var lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
        EditorSceneManager.MoveGameObjectToScene(lightObj, mainMenuScene);

        EditorSceneManager.SaveScene(mainMenuScene, MainMenuScenePath);
        EditorSceneManager.SaveScene(gamePlayScene, GamePlayScenePath);

        Debug.Log($"[ShogiSceneSplitter] 完了。{movedCount}件を01_MainMenu.unityへ移動し(+仮のカメラ/ライトを追加)、" +
                  "両シーンを保存しました。Unity上で両シーンを開いて内容を確認してください。" +
                  "また、ShogiSceneManagerのsceneDatabase(Inspector)で各SceneTypeにsceneNameが" +
                  "正しく設定されているか(MainMenu=\"01_MainMenu\", Game=\"03_GamePlay\"等)を確認し、" +
                  "Build Settingsに01_MainMenu, 02_ModeSelect, 03_GamePlay, 04_Settingsを" +
                  "この順で登録してください(01_MainMenuが最初=起動シーン)。");
    }
}
