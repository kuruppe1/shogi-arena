using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Auth;
using Firebase.Firestore;

namespace ShogiArena.Admin
{
    /// <summary>
    /// 初期管理者セットアップ専用クラス
    /// </summary>
    public class AdminSetupManager : MonoBehaviour
    {
        [Header("環境設定")]
        public EnvironmentType currentEnvironment = EnvironmentType.Development;
        public bool executeSetup = false;
        public bool showDebugInfo = true;
        public bool allowProductionReset = false;

        [Header("データクリア設定")]
        public bool clearExistingData = false;
        public bool createMultipleTestAccounts = false;
        public int testAccountCount = 3;

        [Header("セットアップ状態")]
        public bool isSetupComplete = false;
        public string setupStatus = "待機中";

        [Header("セキュリティ設定")]
        public bool enableTwoFactorAuth = false;
        public bool requireStrongPassword = true;
        public List<string> allowedIPRanges = new List<string>();

        // Firebase参照
        private FirebaseAuth auth;
        private FirebaseFirestore db;

        // セットアップデータ
        private InitialAdminData currentAdminData;
        private List<InitialAdminData> testAccounts;

        // セットアップ進行状況
        private int setupProgress = 0;
        private int totalSetupSteps = 6;

        // イベント
        public static event System.Action<string> OnSetupStatusChanged;
        public static event System.Action<float> OnSetupProgressChanged;
        public static event System.Action<bool> OnSetupCompleted;

        void Start()
        {
            InitializeFirebase();
            currentAdminData = GetEnvironmentAdminData();

            if (createMultipleTestAccounts)
            {
                GenerateTestAccounts();
            }

            if (executeSetup)
            {
                _ = SetupInitialAdmin();
            }
        }

        /// <summary>
        /// Firebase初期化
        /// </summary>
        private void InitializeFirebase()
        {
            auth = FirebaseAuth.DefaultInstance;
            db = FirebaseFirestore.DefaultInstance;

            // デフォルトIPレンジ設定
            if (allowedIPRanges.Count == 0)
            {
                allowedIPRanges.Add("192.168.1.0/24");
                allowedIPRanges.Add("10.0.0.0/8");
                allowedIPRanges.Add("172.16.0.0/12");
            }
        }

        /// <summary>
        /// 環境別管理者データ取得
        /// </summary>
        private InitialAdminData GetEnvironmentAdminData()
        {
            switch (currentEnvironment)
            {
                case EnvironmentType.Development:
                    return new InitialAdminData
                    {
                        email = RequireEnv("SHOGI_ADMIN_DEV_EMAIL"),
                        password = RequireEnv("SHOGI_ADMIN_DEV_PASSWORD"),
                        nickname = "Admin[DEV]",
                        birthDate = default,
                        level = AdminLevel.SuperAdmin,
                        role = "Super Admin (Development)",
                        environment = "development",
                        isTestAccount = true,
                        hiddenAdmin = true
                    };

                case EnvironmentType.Testing:
                    return new InitialAdminData
                    {
                        email = RequireEnv("SHOGI_ADMIN_TEST_EMAIL"),
                        password = RequireEnv("SHOGI_ADMIN_TEST_PASSWORD"),
                        nickname = "Admin[TEST]",
                        birthDate = default,
                        level = AdminLevel.SuperAdmin,
                        role = "Super Admin (Testing)",
                        environment = "testing",
                        isTestAccount = true,
                        hiddenAdmin = true
                    };

                case EnvironmentType.Staging:
                    return new InitialAdminData
                    {
                        email = RequireEnv("SHOGI_ADMIN_STAGING_EMAIL"),
                        password = RequireEnv("SHOGI_ADMIN_STAGING_PASSWORD"),
                        nickname = "Admin[STAGE]",
                        birthDate = default,
                        level = AdminLevel.SuperAdmin,
                        role = "Super Admin (Staging)",
                        environment = "staging",
                        isTestAccount = true,
                        hiddenAdmin = true
                    };

                case EnvironmentType.Production:
                    return new InitialAdminData
                    {
                        email = RequireEnv("SHOGI_ADMIN_PROD_EMAIL"),
                        password = requireStrongPassword ? GenerateStrongPassword() : RequireEnv("SHOGI_ADMIN_PROD_PASSWORD"),
                        nickname = "Admin",
                        birthDate = default,
                        level = AdminLevel.SuperAdmin,
                        role = "Super Admin",
                        environment = "production",
                        isTestAccount = false,
                        hiddenAdmin = true // 一般ユーザーには管理者と分からない
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// 環境変数からシークレットを取得する。未設定なら例外で止める(平文のハードコード値へフォールバックしない)
        /// </summary>
        private static string RequireEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(
                    $"環境変数 {key} が設定されていません。管理者アカウントの認証情報はソースコードに書かず、" +
                    "実行環境の環境変数(またはUnity Editorの場合はローカルの未コミット設定)から読み込む必要があります。");
            }
            return value;
        }

        /// <summary>
        /// テストアカウント群生成
        /// </summary>
        private void GenerateTestAccounts()
        {
            testAccounts = new List<InitialAdminData>();

            var roles = new[] { "Moderator", "CommunityAdmin", "SystemAdmin" };
            var levels = new[] { AdminLevel.Moderator, AdminLevel.CommunityAdmin, AdminLevel.SystemAdmin };

            for (int i = 0; i < testAccountCount; i++)
            {
                var roleIndex = i % roles.Length;
                testAccounts.Add(new InitialAdminData
                {
                    email = $"test{i + 1}+{currentEnvironment.ToString().ToLower()}@shogi-arena.com",
                    password = GenerateStrongPassword(),
                    nickname = $"テスト管理者{i + 1}",
                    birthDate = new DateTime(1990, 1, 1).AddDays(i * 100),
                    level = levels[roleIndex],
                    role = $"{roles[roleIndex]} (Test)",
                    environment = currentEnvironment.ToString().ToLower(),
                    isTestAccount = true,
                    hiddenAdmin = false
                });
            }
        }

        /// <summary>
        /// 強力なパスワード生成
        /// </summary>
        private string GenerateStrongPassword()
        {
            const string upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowerChars = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string symbols = "!@#$%^&*";

            var random = new System.Random();
            var password = "";

            // 各文字種から最低1文字
            password += upperChars[random.Next(upperChars.Length)];
            password += lowerChars[random.Next(lowerChars.Length)];
            password += digits[random.Next(digits.Length)];
            password += symbols[random.Next(symbols.Length)];

            // 残りの文字をランダムに追加
            const string allChars = upperChars + lowerChars + digits + symbols;
            for (int i = 4; i < 16; i++)
            {
                password += allChars[random.Next(allChars.Length)];
            }

            // シャッフル
            var chars = password.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                int j = random.Next(i, chars.Length);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            var finalPassword = new string(chars);
            LogInfo("強力なパスワードを生成しました(ログには出力しません。呼び出し元で安全に保管してください)");
            return finalPassword;
        }

        /// <summary>
        /// 初期管理者セットアップ実行
        /// </summary>
        public async Task SetupInitialAdmin()
        {
            if (isSetupComplete && !allowProductionReset)
            {
                LogInfo("セットアップは既に完了しています");
                return;
            }

            try
            {
                setupProgress = 0;
                UpdateSetupStatus("セットアップ開始...");
                LogInfo("=== 初期管理者セットアップ開始 ===");

                // Step 1: 既存データクリア（必要に応じて）
                if (clearExistingData)
                {
                    UpdateSetupStatus("既存データクリア中...");
                    await ClearExistingData();
                    UpdateProgress();
                }

                // Step 2: Firebase認証アカウント作成
                UpdateSetupStatus("Firebase認証アカウント作成中...");
                string userId = await CreateFirebaseAuthAccount();
                if (string.IsNullOrEmpty(userId))
                {
                    throw new Exception("Firebase認証アカウント作成に失敗");
                }
                UpdateProgress();

                // Step 3: 管理者レコード作成
                UpdateSetupStatus("管理者レコード作成中...");
                await CreateAdminRecord(userId);
                UpdateProgress();

                // Step 4: ゲームアカウント作成（隠密管理者仕様）
                UpdateSetupStatus("ゲームアカウント作成中...");
                await CreateGameAccount(userId);
                UpdateProgress();

                // Step 5: 初期設定作成
                UpdateSetupStatus("初期設定作成中...");
                await CreateInitialSettings();
                UpdateProgress();

                // Step 6: テストアカウント作成（必要に応じて）
                if (createMultipleTestAccounts && testAccounts != null)
                {
                    UpdateSetupStatus("テストアカウント作成中...");
                    await CreateTestAccounts();
                }
                UpdateProgress();

                // セットアップ完了
                UpdateSetupStatus("セットアップ完了");
                isSetupComplete = true;

                LogInfo("=== 初期管理者セットアップ完了 ===");
                LogInfo($"管理者ID: {userId}");
                LogInfo($"メール: {currentAdminData.email}");
                LogInfo($"権限レベル: {currentAdminData.level}");
                LogInfo($"隠密モード: {currentAdminData.hiddenAdmin}");

                if (createMultipleTestAccounts)
                {
                    LogInfo($"テストアカウント: {testAccounts.Count}個作成");
                }

                OnSetupCompleted?.Invoke(true);

                // セットアップ完了をログに記録
                if (AdminLogManager.Instance != null)
                {
                    AdminLogManager.Instance.LogAdminAction("initial_setup",
                        $"初期管理者セットアップ完了 (環境: {currentEnvironment})", "system", "setup");
                }
            }
            catch (Exception ex)
            {
                UpdateSetupStatus($"セットアップエラー: {ex.Message}");
                LogError($"初期管理者セットアップエラー: {ex.Message}");
                OnSetupCompleted?.Invoke(false);
            }
        }

        /// <summary>
        /// テストアカウント作成
        /// </summary>
        private async Task CreateTestAccounts()
        {
            foreach (var testAccount in testAccounts)
            {
                try
                {
                    // Firebase認証アカウント作成
                    var result = await auth.CreateUserWithEmailAndPasswordAsync(
                        testAccount.email, testAccount.password);

                    if (result?.User != null)
                    {
                        var userId = result.User.UserId;

                        // 管理者レコード作成
                        await CreateAdminRecordForTestAccount(userId, testAccount);

                        // ゲームアカウント作成
                        await CreateGameAccountForTestAccount(userId, testAccount);

                        LogInfo($"テストアカウント作成完了: {testAccount.email}");
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"テストアカウント作成失敗: {testAccount.email} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// テストアカウント用管理者レコード作成
        /// </summary>
        private async Task CreateAdminRecordForTestAccount(string userId, InitialAdminData testData)
        {
            var adminData = new AdminData
            {
                adminId = userId,
                email = testData.email,
                displayName = testData.nickname,
                level = testData.level,
                role = testData.role,
                department = "テスト部門",
                isActive = true,
                createdAt = DateTime.UtcNow,
                lastLoginAt = DateTime.UtcNow
            };

            // 権限設定
            adminData.permissions = new AdminPermissions();
            adminData.permissions.SetPermissionsByLevel(testData.level);

            // 制限設定（テストアカウントはより制限的）
            adminData.restrictions = new AdminRestrictions
            {
                ipWhitelist = new List<string>(allowedIPRanges),
                timeRestrictions = new TimeRestriction { startHour = 9, endHour = 18 },
                maxSessionDuration = 120 // 2時間
            };

            await db.Collection("admins").Document(userId).SetAsync(adminData);
        }

        /// <summary>
        /// テストアカウント用ゲームアカウント作成
        /// </summary>
        private async Task CreateGameAccountForTestAccount(string userId, InitialAdminData testData)
        {
            var userData = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["profile"] = new Dictionary<string, object>
                {
                    ["nickname"] = testData.nickname,
                    ["email"] = testData.email,
                    ["playerId"] = $"TEST{UnityEngine.Random.Range(100, 999)}",
                    ["environment"] = testData.environment,
                    ["isTestAccount"] = true,
                    ["hiddenAdmin"] = testData.hiddenAdmin,
                    ["createdAt"] = DateTime.UtcNow,
                    ["lastLoginAt"] = DateTime.UtcNow,
                    ["isOnline"] = false,
                    ["accountType"] = "admin_test"
                },
                ["gameStats"] = new Dictionary<string, object>
                {
                    ["rating"] = 1200,
                    ["rank"] = "4級",
                    ["dan"] = 0,
                    ["kyu"] = 4,
                    ["totalGames"] = 0,
                    ["wins"] = 0,
                    ["losses"] = 0,
                    ["draws"] = 0,
                    ["winRate"] = 0.0f
                },
                ["specialFeatures"] = new Dictionary<string, object>
                {
                    ["unlimitedGames"] = false,
                    ["adminBadge"] = !testData.hiddenAdmin,
                    ["specialEmotes"] = false,
                    ["priorityMatching"] = false,
                    ["testingTools"] = true,
                    ["hiddenPrivileges"] = testData.hiddenAdmin
                },
                ["membership"] = new Dictionary<string, object>
                {
                    ["type"] = "test",
                    ["startDate"] = DateTime.UtcNow,
                    ["endDate"] = DateTime.UtcNow.AddMonths(1),
                    ["autoRenewal"] = false
                }
            };

            await db.Collection("users").Document(userId).SetAsync(userData);
        }

        /// <summary>
        /// Firebase認証アカウント作成
        /// </summary>
        private async Task<string> CreateFirebaseAuthAccount()
        {
            try
            {
                // 既存アカウント確認
                var existingUser = await CheckExistingAccount();
                if (existingUser != null)
                {
                    LogInfo("既存アカウントを使用します");
                    return existingUser.UserId;
                }

                // 新規アカウント作成
                var result = await auth.CreateUserWithEmailAndPasswordAsync(
                    currentAdminData.email, currentAdminData.password);

                if (result?.User != null)
                {
                    LogInfo("Firebase認証アカウント作成完了");

                    // メール確認（本番環境のみ）
                    if (currentEnvironment == EnvironmentType.Production)
                    {
                        await result.User.SendEmailVerificationAsync();
                        LogInfo("確認メールを送信しました");
                    }

                    return result.User.UserId;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"Firebase認証アカウント作成エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 管理者レコード作成
        /// </summary>
        private async Task CreateAdminRecord(string userId)
        {
            try
            {
                var adminData = new AdminData
                {
                    adminId = userId,
                    email = currentAdminData.email,
                    displayName = currentAdminData.nickname,
                    level = currentAdminData.level,
                    role = currentAdminData.role,
                    department = "開発・運営統括",
                    isActive = true,
                    createdAt = DateTime.UtcNow,
                    lastLoginAt = DateTime.UtcNow
                };

                // 権限設定
                adminData.permissions = new AdminPermissions();
                adminData.permissions.SetPermissionsByLevel(currentAdminData.level);

                // 制限設定（環境に応じて調整）
                adminData.restrictions = new AdminRestrictions
                {
                    ipWhitelist = new List<string>(allowedIPRanges),
                    timeRestrictions = new TimeRestriction
                    {
                        startHour = currentEnvironment == EnvironmentType.Production ? 9 : 0,
                        endHour = currentEnvironment == EnvironmentType.Production ? 22 : 23
                    },
                    maxSessionDuration = currentAdminData.isTestAccount ? 240 : 480
                };

                await db.Collection("admins").Document(userId).SetAsync(adminData);
                LogInfo("管理者レコード作成完了");
            }
            catch (Exception ex)
            {
                LogError($"管理者レコード作成エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// ゲームアカウント作成（隠密管理者仕様）
        /// </summary>
        private async Task CreateGameAccount(string userId)
        {
            try
            {
                var userData = new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["profile"] = new Dictionary<string, object>
                    {
                        ["nickname"] = currentAdminData.nickname,
                        ["email"] = currentAdminData.email,
                        ["playerId"] = currentAdminData.isTestAccount ? "TEST001" : GenerateRandomPlayerId(),
                        ["environment"] = currentAdminData.environment,
                        ["isTestAccount"] = currentAdminData.isTestAccount,
                        ["hiddenAdmin"] = currentAdminData.hiddenAdmin,
                        ["createdAt"] = DateTime.UtcNow,
                        ["lastLoginAt"] = DateTime.UtcNow,
                        ["isOnline"] = true,
                        ["accountType"] = currentAdminData.hiddenAdmin ? "premium" : "super_admin",
                        ["avatarUrl"] = "", // デフォルトアバター
                        ["bio"] = currentAdminData.hiddenAdmin ? "将棋が好きです" : "システム管理者"
                    },
                    ["gameStats"] = new Dictionary<string, object>
                    {
                        ["rating"] = currentAdminData.isTestAccount ? 1000 : 1500,
                        ["rank"] = CalculateNormalRank(currentAdminData.isTestAccount ? 1000 : 1500),
                        ["dan"] = 0,
                        ["kyu"] = 30,
                        ["totalGames"] = 0,
                        ["wins"] = 0,
                        ["losses"] = 0,
                        ["draws"] = 0,
                        ["winRate"] = 0.0f,
                        ["highestRating"] = currentAdminData.isTestAccount ? 1000 : 1500,
                        ["ratingHistory"] = new List<object>()
                    },
                    ["specialFeatures"] = new Dictionary<string, object>
                    {
                        ["unlimitedGames"] = true,
                        ["adminBadge"] = !currentAdminData.hiddenAdmin,
                        ["specialEmotes"] = !currentAdminData.hiddenAdmin,
                        ["priorityMatching"] = !currentAdminData.hiddenAdmin,
                        ["testingTools"] = currentAdminData.isTestAccount,
                        ["hiddenPrivileges"] = currentAdminData.hiddenAdmin,
                        ["bypassRestrictions"] = true,
                        ["debugMode"] = currentAdminData.isTestAccount
                    },
                    ["membership"] = new Dictionary<string, object>
                    {
                        ["type"] = currentAdminData.hiddenAdmin ? "premium" : "admin",
                        ["startDate"] = DateTime.UtcNow,
                        ["endDate"] = DateTime.UtcNow.AddYears(10),
                        ["autoRenewal"] = false,
                        ["benefits"] = new List<string> { "unlimited_games", "priority_support", "exclusive_events" }
                    },
                    ["displaySettings"] = new Dictionary<string, object>
                    {
                        ["showAdminStatus"] = !currentAdminData.hiddenAdmin,
                        ["showSpecialEffects"] = !currentAdminData.hiddenAdmin,
                        ["appearAsNormalUser"] = currentAdminData.hiddenAdmin,
                        ["hideFromLeaderboards"] = currentAdminData.hiddenAdmin,
                        ["allowDirectMessages"] = true
                    },
                    ["security"] = new Dictionary<string, object>
                    {
                        ["twoFactorEnabled"] = enableTwoFactorAuth,
                        ["lastPasswordChange"] = DateTime.UtcNow,
                        ["loginAttempts"] = 0,
                        ["lockedUntil"] = DateTime.MinValue,
                        ["securityLevel"] = "maximum"
                    }
                };

                await db.Collection("users").Document(userId).SetAsync(userData);
                LogInfo("隠密管理者ゲームアカウント作成完了");
            }
            catch (Exception ex)
            {
                LogError($"ゲームアカウント作成エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 初期設定作成
        /// </summary>
        private async Task CreateInitialSettings()
        {
            try
            {
                var systemConfig = new Dictionary<string, object>
                {
                    ["version"] = "1.0.0",
                    ["environment"] = currentAdminData.environment,
                    ["setupDate"] = DateTime.UtcNow,
                    ["setupBy"] = currentAdminData.email,
                    ["adminSystemEnabled"] = true,
                    ["debugMode"] = currentAdminData.isTestAccount,
                    ["maintenanceMode"] = false,
                    ["allowRegistration"] = true,
                    ["maxConcurrentUsers"] = currentEnvironment == EnvironmentType.Production ? 10000 : 100,
                    ["features"] = new Dictionary<string, object>
                    {
                        ["matchmaking"] = true,
                        ["tournament"] = true,
                        ["chat"] = true,
                        ["leaderboard"] = true,
                        ["premium"] = true
                    },
                    ["security"] = new Dictionary<string, object>
                    {
                        ["requireEmailVerification"] = currentEnvironment == EnvironmentType.Production,
                        ["maxLoginAttempts"] = 5,
                        ["sessionTimeout"] = 1440, // 24時間
                        ["passwordMinLength"] = requireStrongPassword ? 12 : 8,
                        ["allowedIPRanges"] = allowedIPRanges
                    }
                };

                await db.Collection("systemConfig").Document("general").SetAsync(systemConfig);

                // 管理者システム設定
                var adminConfig = new Dictionary<string, object>
                {
                    ["setupComplete"] = true,
                    ["initialAdminCreated"] = DateTime.UtcNow,
                    ["environment"] = currentAdminData.environment,
                    ["testAccountsCreated"] = createMultipleTestAccounts ? testAccountCount : 0,
                    ["securityLevel"] = currentEnvironment == EnvironmentType.Production ? "high" : "medium",
                    ["auditingEnabled"] = true,
                    ["autoCleanupEnabled"] = true,
                    ["alertingEnabled"] = true
                };

                await db.Collection("systemConfig").Document("admin").SetAsync(adminConfig);

                LogInfo("初期設定作成完了");
            }
            catch (Exception ex)
            {
                LogError($"初期設定作成エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 既存アカウント確認
        /// </summary>
        private async Task<FirebaseUser> CheckExistingAccount()
        {
            try
            {
                var result = await auth.SignInWithEmailAndPasswordAsync(
                    currentAdminData.email, currentAdminData.password);

                if (result?.User != null)
                {
                    auth.SignOut(); // 一度サインアウト
                    return result.User;
                }

                return null;
            }
            catch
            {
                return null; // アカウントが存在しない
            }
        }

        /// <summary>
        /// 既存データクリア
        /// </summary>
        private async Task ClearExistingData()
        {
            if (currentEnvironment == EnvironmentType.Production && !allowProductionReset)
            {
                LogWarning("本番環境でのデータクリアは許可されていません");
                return;
            }

            try
            {
                LogInfo("既存データクリア開始...");

                // 管理者データクリア
                var adminQuery = db.Collection("admins").WhereEqualTo("email", currentAdminData.email);
                var adminSnapshot = await adminQuery.GetSnapshotAsync();

                foreach (var doc in adminSnapshot.Documents)
                {
                    await doc.Reference.DeleteAsync();
                    LogInfo($"管理者データ削除: {doc.Id}");
                }

                // ユーザーデータクリア
                var userQuery = db.Collection("users").WhereEqualTo("profile.email", currentAdminData.email);
                var userSnapshot = await userQuery.GetSnapshotAsync();

                foreach (var doc in userSnapshot.Documents)
                {
                    await doc.Reference.DeleteAsync();
                    LogInfo($"ユーザーデータ削除: {doc.Id}");
                }

                // テストアカウントのデータクリア
                if (createMultipleTestAccounts && testAccounts != null)
                {
                    foreach (var testAccount in testAccounts)
                    {
                        await ClearTestAccountData(testAccount.email);
                    }
                }

                LogInfo("既存データクリア完了");
            }
            catch (Exception ex)
            {
                LogError($"データクリアエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// テストアカウントデータクリア
        /// </summary>
        private async Task ClearTestAccountData(string email)
        {
            try
            {
                var adminQuery = db.Collection("admins").WhereEqualTo("email", email);
                var adminSnapshot = await adminQuery.GetSnapshotAsync();

                foreach (var doc in adminSnapshot.Documents)
                {
                    await doc.Reference.DeleteAsync();
                }

                var userQuery = db.Collection("users").WhereEqualTo("profile.email", email);
                var userSnapshot = await userQuery.GetSnapshotAsync();

                foreach (var doc in userSnapshot.Documents)
                {
                    await doc.Reference.DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                LogWarning($"テストアカウントデータクリアエラー ({email}): {ex.Message}");
            }
        }

        /// <summary>
        /// セットアップ状態更新
        /// </summary>
        private void UpdateSetupStatus(string status)
        {
            setupStatus = status;
            OnSetupStatusChanged?.Invoke(status);
        }

        /// <summary>
        /// セットアップ進行状況更新
        /// </summary>
        private void UpdateProgress()
        {
            setupProgress++;
            float progress = (float)setupProgress / totalSetupSteps;
            OnSetupProgressChanged?.Invoke(progress);
        }

        /// <summary>
        /// 通常の段級位計算（管理者専用表示なし）
        /// </summary>
        private string CalculateNormalRank(int rating)
        {
            if (rating >= 2400) return "5段";
            if (rating >= 2200) return "4段";
            if (rating >= 2000) return "3段";
            if (rating >= 1800) return "2段";
            if (rating >= 1600) return "1段";
            if (rating >= 1500) return "1級";
            if (rating >= 1400) return "2級";
            if (rating >= 1300) return "3級";
            if (rating >= 1200) return "4級";
            if (rating >= 1100) return "5級";
            if (rating >= 1000) return "6級";
            if (rating >= 900) return "7級";
            if (rating >= 800) return "8級";
            if (rating >= 700) return "9級";
            return "10級";
        }

        /// <summary>
        /// ランダムプレイヤーID生成（管理者専用IDを隠すため）
        /// </summary>
        private string GenerateRandomPlayerId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new System.Random();
            var result = new char[8];

            for (int i = 0; i < 8; i++)
            {
                result[i] = chars[random.Next(chars.Length)];
            }

            return new string(result);
        }

        /// <summary>
        /// セットアップ完了後のクリーンアップ
        /// </summary>
        public void CompleteSetup()
        {
            executeSetup = false;
            setupStatus = "セットアップ完了 - 手動実行モード";
            LogInfo("セットアップフラグを無効化しました。今後は手動実行のみ可能です。");

            // セットアップ完了ログ
            if (AdminLogManager.Instance != null)
            {
                AdminLogManager.Instance.LogAdminAction("setup_complete",
                    $"初期セットアップ完了処理実行 (環境: {currentEnvironment})", "system", "complete");
            }
        }

        /// <summary>
        /// 既存管理者確認
        /// </summary>
        public async Task CheckExistingAdmin()
        {
            try
            {
                LogInfo("=== 既存管理者確認開始 ===");

                var adminQuery = db.Collection("admins").WhereEqualTo("email", currentAdminData.email);
                var adminSnapshot = await adminQuery.GetSnapshotAsync();

                if (adminSnapshot.Count > 0)
                {
                    foreach (var doc in adminSnapshot.Documents)
                    {
                        var adminData = doc.ConvertTo<AdminData>();
                        LogInfo($"既存管理者発見:");
                        LogInfo($"  ID: {doc.Id}");
                        LogInfo($"  名前: {adminData.displayName}");
                        LogInfo($"  権限: {adminData.level}");
                        LogInfo($"  アクティブ: {adminData.isActive}");
                        LogInfo($"  最終ログイン: {adminData.lastLoginAt}");
                        LogInfo($"  部署: {adminData.department}");
                        LogInfo($"  役職: {adminData.role}");
                    }
                }
                else
                {
                    LogInfo("既存管理者が見つかりませんでした");
                }

                // テストアカウントも確認
                if (createMultipleTestAccounts && testAccounts != null)
                {
                    LogInfo("=== テストアカウント確認 ===");
                    foreach (var testAccount in testAccounts)
                    {
                        var testQuery = db.Collection("admins").WhereEqualTo("email", testAccount.email);
                        var testSnapshot = await testQuery.GetSnapshotAsync();

                        if (testSnapshot.Count > 0)
                        {
                            LogInfo($"テストアカウント存在: {testAccount.email}");
                        }
                        else
                        {
                            LogInfo($"テストアカウント未作成: {testAccount.email}");
                        }
                    }
                }

                LogInfo("=== 既存管理者確認完了 ===");
            }
            catch (Exception ex)
            {
                LogError($"既存管理者確認エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// セットアップ検証
        /// </summary>
        public async Task<bool> ValidateSetup()
        {
            try
            {
                LogInfo("=== セットアップ検証開始 ===");
                bool isValid = true;

                // 1. 管理者アカウント存在確認
                var adminQuery = db.Collection("admins").WhereEqualTo("email", currentAdminData.email);
                var adminSnapshot = await adminQuery.GetSnapshotAsync();

                if (adminSnapshot.Count == 0)
                {
                    LogError("管理者アカウントが見つかりません");
                    isValid = false;
                }

                // 2. ゲームアカウント存在確認
                var userQuery = db.Collection("users").WhereEqualTo("profile.email", currentAdminData.email);
                var userSnapshot = await userQuery.GetSnapshotAsync();

                if (userSnapshot.Count == 0)
                {
                    LogError("ゲームアカウントが見つかりません");
                    isValid = false;
                }

                // 3. システム設定確認
                var configDoc = await db.Collection("systemConfig").Document("general").GetSnapshotAsync();
                if (!configDoc.Exists)
                {
                    LogError("システム設定が見つかりません");
                    isValid = false;
                }

                // 4. Firebase認証確認
                try
                {
                    var authResult = await auth.SignInWithEmailAndPasswordAsync(
                        currentAdminData.email, currentAdminData.password);

                    if (authResult?.User == null)
                    {
                        LogError("Firebase認証に失敗");
                        isValid = false;
                    }
                    else
                    {
                        auth.SignOut();
                        LogInfo("Firebase認証確認完了");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Firebase認証確認エラー: {ex.Message}");
                    isValid = false;
                }

                LogInfo($"=== セットアップ検証完了: {(isValid ? "成功" : "失敗")} ===");
                return isValid;
            }
            catch (Exception ex)
            {
                LogError($"セットアップ検証エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// セットアップ情報取得
        /// </summary>
        public Dictionary<string, object> GetSetupInfo()
        {
            return new Dictionary<string, object>
            {
                ["environment"] = currentEnvironment.ToString(),
                ["isComplete"] = isSetupComplete,
                ["status"] = setupStatus,
                ["progress"] = (float)setupProgress / totalSetupSteps,
                ["adminEmail"] = currentAdminData?.email,
                ["adminNickname"] = currentAdminData?.nickname,
                ["isHiddenAdmin"] = currentAdminData?.hiddenAdmin ?? false,
                ["testAccountsEnabled"] = createMultipleTestAccounts,
                ["testAccountCount"] = testAccountCount,
                ["securityLevel"] = currentEnvironment == EnvironmentType.Production ? "high" : "medium",
                ["allowedIPRanges"] = allowedIPRanges.Count,
                ["strongPasswordEnabled"] = requireStrongPassword,
                ["twoFactorEnabled"] = enableTwoFactorAuth
            };
        }

        /// <summary>
        /// 緊急リセット
        /// </summary>
        public async Task EmergencyReset()
        {
            if (currentEnvironment == EnvironmentType.Production && !allowProductionReset)
            {
                LogError("本番環境での緊急リセットは許可されていません");
                return;
            }

            try
            {
                LogWarning("=== 緊急リセット開始 ===");

                // すべてのデータをクリア
                clearExistingData = true;
                await ClearExistingData();

                // セットアップ状態をリセット
                isSetupComplete = false;
                setupProgress = 0;
                setupStatus = "リセット完了 - 再セットアップが必要";

                LogWarning("=== 緊急リセット完了 ===");
                UpdateSetupStatus("緊急リセット完了");

                // 緊急リセットをログに記録
                if (AdminLogManager.Instance != null)
                {
                    await AdminLogManager.Instance.LogCriticalAction("emergency_reset",
                        $"緊急リセット実行 (環境: {currentEnvironment})", "system", "reset");
                }
            }
            catch (Exception ex)
            {
                LogError($"緊急リセットエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 環境切り替え
        /// </summary>
        public void SwitchEnvironment(EnvironmentType newEnvironment)
        {
            if (isSetupComplete && currentEnvironment != newEnvironment)
            {
                LogWarning($"環境切り替え: {currentEnvironment} → {newEnvironment}");
                currentEnvironment = newEnvironment;
                currentAdminData = GetEnvironmentAdminData();

                if (createMultipleTestAccounts)
                {
                    GenerateTestAccounts();
                }

                LogInfo("環境切り替え完了。再セットアップが推奨されます。");
            }
        }

        void OnGUI()
        {
            if (showDebugInfo)
            {
                float boxX = 10;
                float boxY = 10;
                float boxWidth = 500;
                float boxHeight = 420;

                GUI.Box(new Rect(boxX, boxY, boxWidth, boxHeight), "初期管理者セットアップ");

                // 基本情報表示
                GUI.Label(new Rect(boxX + 10, boxY + 30, boxWidth - 20, 25),
                    $"環境: {currentEnvironment}");
                GUI.Label(new Rect(boxX + 10, boxY + 55, boxWidth - 20, 25),
                    $"管理者: {currentAdminData?.nickname}");
                GUI.Label(new Rect(boxX + 10, boxY + 80, boxWidth - 20, 25),
                    $"メール: {currentAdminData?.email}");
                GUI.Label(new Rect(boxX + 10, boxY + 105, boxWidth - 20, 25),
                    $"隠密モード: {currentAdminData?.hiddenAdmin}");
                GUI.Label(new Rect(boxX + 10, boxY + 130, boxWidth - 20, 25),
                    $"ステータス: {setupStatus}");

                // 進行状況バー
                if (setupProgress > 0)
                {
                    float progress = (float)setupProgress / totalSetupSteps;
                    GUI.Box(new Rect(boxX + 10, boxY + 155, boxWidth - 20, 15), "");
                    GUI.Box(new Rect(boxX + 10, boxY + 155, (boxWidth - 20) * progress, 15), "");
                    GUI.Label(new Rect(boxX + 10, boxY + 175, boxWidth - 20, 20),
                        $"進行状況: {setupProgress}/{totalSetupSteps} ({progress * 100:F0}%)");
                }

                // メインボタン
                if (GUI.Button(new Rect(boxX + 10, boxY + 200, 150, 30), "管理者セットアップ実行"))
                {
                    executeSetup = true;
                    _ = SetupInitialAdmin();
                }

                if (GUI.Button(new Rect(boxX + 170, boxY + 200, 150, 30), "セットアップ検証"))
                {
                    _ = ValidateSetup();
                }

                if (GUI.Button(new Rect(boxX + 330, boxY + 200, 150, 30), "セットアップ完了"))
                {
                    CompleteSetup();
                }

                // 設定トグル
                clearExistingData = GUI.Toggle(new Rect(boxX + 10, boxY + 240, 200, 25),
                    clearExistingData, "既存データクリア");

                createMultipleTestAccounts = GUI.Toggle(new Rect(boxX + 220, boxY + 240, 200, 25),
                    createMultipleTestAccounts, "テストアカウント作成");

                requireStrongPassword = GUI.Toggle(new Rect(boxX + 10, boxY + 265, 200, 25),
                    requireStrongPassword, "強力なパスワード");

                enableTwoFactorAuth = GUI.Toggle(new Rect(boxX + 220, boxY + 265, 200, 25),
                    enableTwoFactorAuth, "二要素認証");

                // 管理ボタン
                if (GUI.Button(new Rect(boxX + 10, boxY + 295, 120, 25), "既存管理者確認"))
                {
                    _ = CheckExistingAdmin();
                }

                // 本番環境以外でのみ表示
                if (currentEnvironment != EnvironmentType.Production)
                {
                    if (GUI.Button(new Rect(boxX + 140, boxY + 295, 120, 25), "テストデータクリア"))
                    {
                        _ = ClearExistingData();
                    }

                    if (GUI.Button(new Rect(boxX + 270, boxY + 295, 120, 25), "緊急リセット"))
                    {
                        _ = EmergencyReset();
                    }
                }

                // 環境切り替えボタン
                GUI.Label(new Rect(boxX + 10, boxY + 325, 100, 25), "環境切り替え:");

                if (GUI.Button(new Rect(boxX + 10, boxY + 345, 80, 25), "DEV"))
                    SwitchEnvironment(EnvironmentType.Development);
                if (GUI.Button(new Rect(boxX + 100, boxY + 345, 80, 25), "TEST"))
                    SwitchEnvironment(EnvironmentType.Testing);
                if (GUI.Button(new Rect(boxX + 190, boxY + 345, 80, 25), "STAGE"))
                    SwitchEnvironment(EnvironmentType.Staging);
                if (GUI.Button(new Rect(boxX + 280, boxY + 345, 80, 25), "PROD"))
                    SwitchEnvironment(EnvironmentType.Production);

                // セキュリティ情報
                GUI.Label(new Rect(boxX + 10, boxY + 375, boxWidth - 20, 25),
                    $"💡 セキュリティ: {(currentEnvironment == EnvironmentType.Production ? "高" : "中")} | IP制限: {allowedIPRanges.Count}個");

                // セットアップ完了状態表示
                if (isSetupComplete)
                {
                    GUI.Label(new Rect(boxX + 10, boxY + 395, boxWidth - 20, 25),
                        "✅ セットアップ完了済み - システム運用可能");
                }
                else
                {
                    GUI.Label(new Rect(boxX + 10, boxY + 395, boxWidth - 20, 25),
                        "⚠️ セットアップ未完了 - システム設定が必要");
                }
            }
        }

        void LogInfo(string message)
        {
            if (showDebugInfo)
                Debug.Log($"[AdminSetup] ✅ {message}");
        }

        void LogWarning(string message)
        {
            if (showDebugInfo)
                Debug.LogWarning($"[AdminSetup] ⚠️ {message}");
        }

        void LogError(string message)
        {
            Debug.LogError($"[AdminSetup] ❌ {message}");
        }

        void OnDestroy()
        {
            // 進行中のセットアップがある場合は完了を待つ
            if (setupProgress > 0 && setupProgress < totalSetupSteps)
            {
                LogWarning("セットアップが進行中です。完了をお待ちください。");
            }
        }
    }
}