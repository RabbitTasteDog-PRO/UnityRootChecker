using System;
using System.IO;
using System.Text;
using UnityEngine;

public class RootCheckerManager
{
    private static readonly string[] SuspectPaths =
    {
        "/system/bin/su",
        "/system/xbin/su",
        "/sbin/su",
        "/system/app/Superuser.apk",
        "/system/app/SuperSU.apk",
        "/system/app/Magisk.apk",
        "/data/local/su",
        "/data/local/bin/su",
        "/data/local/xbin/su",
        "/data/data/com.noshufou.android.su",
    };

    private static readonly string[] SuspectPackages =
    {
        "com.topjohnwu.magisk",
        "eu.chainfire.supersu",
        "com.noshufou.android.su",
        "com.koushikdutta.superuser",
        "com.thirdparty.superuser",
    };

    // ---------------------------------------------------------
    // Public API
    // ---------------------------------------------------------

    /// <summary>
    /// 디바이스/환경 정보 로그용 문자열
    /// </summary>
    public string GetData()
    {
        var sb = new StringBuilder(256);

        sb.Append(Application.installerName).Append("/");
        sb.Append(Application.installMode).Append("/");
        sb.Append(Application.buildGUID).Append("/");
        sb.Append("Genuine:").Append(Application.genuine);

        if (Application.platform != RuntimePlatform.Android)
        {
            return sb.ToString();
        }

        var build = new AndroidJavaClass("android.os.Build");

        string fingerprint = SafeGetStatic(build, "FINGERPRINT");
        string model = SafeGetStatic(build, "MODEL");
        string manufacturer = SafeGetStatic(build, "MANUFACTURER");
        string brand = SafeGetStatic(build, "BRAND");
        string device = SafeGetStatic(build, "DEVICE");
        string product = SafeGetStatic(build, "PRODUCT");
        string hardware = SafeGetStatic(build, "HARDWARE");
        string tags = SafeGetStatic(build, "TAGS");

        bool isEmu = IsEmulator(build, fingerprint, model, manufacturer, brand, device, product, hardware);

        bool isRooted = IsRooted(out string rootReason);

        sb.Append("/");
        sb.Append("Rooted:").Append(isRooted);

        if (isRooted == true)
        {
            sb.Append("/");
            sb.Append("RootReason:").Append(rootReason);
        }

        sb.Append("/");
        sb.Append("Emulator:").Append(isEmu);
        sb.Append("/");
        sb.Append("Model:").Append(model);
        sb.Append("/");
        sb.Append("Manufacturer:").Append(manufacturer);
        sb.Append("/");
        sb.Append("Brand:").Append(brand);
        sb.Append("/");
        sb.Append("Device:").Append(device);
        sb.Append("/");
        sb.Append("Fingerprint:").Append(fingerprint);
        sb.Append("/");
        sb.Append("Product:").Append(product);
        sb.Append("/");
        sb.Append("Hardware:").Append(hardware);
        sb.Append("/");
        sb.Append("Tags:").Append(tags);

        return sb.ToString();
    }

    /// <summary>
    /// 루팅(=실제로 root 권한 획득 가능 포함) 판정
    /// </summary>
    public bool IsRooted(out string reason)
    {
        reason = string.Empty;

        if (Application.platform != RuntimePlatform.Android)
        {
            return false;
        }

        // 1) 가장 강함: su -c id 실행이 되고 uid=0 이면 거의 확정
        if (CanGetRootBySu(out string suReason) == true)
        {
            reason = suReason;
            return true;
        }

        // 2) getprop 기반 시그널(에뮬/개발빌드에서 자주 보임)
        string roSecure = GetProp("ro.secure");
        if (roSecure == "0")
        {
            reason = "prop:ro.secure=0";
            return true;
        }

        string roDebuggable = GetProp("ro.debuggable");
        if (roDebuggable == "1")
        {
            reason = "prop:ro.debuggable=1";
            return true;
        }

        // 3) 흔적 파일(보조)
        for (int i = 0; i < SuspectPaths.Length; i++)
        {
            if (File.Exists(SuspectPaths[i]) == true)
            {
                reason = $"file:{SuspectPaths[i]}";
                return true;
            }
        }

        // 4) 루팅 앱 패키지(선택)
        string pkg = FindInstalledRootPackage();
        if (string.IsNullOrEmpty(pkg) == false)
        {
            reason = $"package:{pkg}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// “에뮬레이터는 실행 허용”을 전제로, 민감 기능(결제/랭킹/거래 등)만 제한하고 싶을 때 쓰는 예시
    /// </summary>
    public bool ShouldRestrictSensitiveFeatures(out string reason)
    {
        reason = string.Empty;

        if (Application.platform != RuntimePlatform.Android)
        {
            return false;
        }

        bool isEmu = IsEmulator();
        bool rooted = IsRooted(out string rootReason);

        // 에뮬레이터 실행은 허용하지만, 에뮬 + 루팅이면 제한
        if (isEmu == true)
        {
            if (rooted == true)
            {
                reason = $"에뮬레이터의 루팅을 사용하는 유저입니다:{rootReason}";
                return true;
            }

            return false;
        }

        // 실기에서는 더 강하게(정책에 맞게)
        if (rooted == true)
        {
            reason = $"디바이서의 루팅을 사용하는 유저입니다:{rootReason}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// 기존 코드 호환용: 단순 에뮬 판정
    /// </summary>
    public bool IsEmulator()
    {
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return true;
        }

        if (Application.platform != RuntimePlatform.Android)
        {
            return false;
        }

        var build = new AndroidJavaClass("android.os.Build");

        string fingerprint = SafeGetStatic(build, "FINGERPRINT");
        string model = SafeGetStatic(build, "MODEL");
        string manufacturer = SafeGetStatic(build, "MANUFACTURER");
        string brand = SafeGetStatic(build, "BRAND");
        string device = SafeGetStatic(build, "DEVICE");
        string product = SafeGetStatic(build, "PRODUCT");
        string hardware = SafeGetStatic(build, "HARDWARE");

        return IsEmulator(build, fingerprint, model, manufacturer, brand, device, product, hardware);
    }

    // ---------------------------------------------------------
    // Emulator detection (internal)
    // ---------------------------------------------------------

    private bool IsEmulator(
        AndroidJavaClass build,
        string fingerprint,
        string model,
        string manufacturer,
        string brand,
        string device,
        string product,
        string hardware)
    {
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return true;
        }

        if (Application.platform != RuntimePlatform.Android)
        {
            return false;
        }

        if (fingerprint.Contains("generic") == true || fingerprint.Contains("unknown") == true)
        {
            return true;
        }

        if (model.Contains("google_sdk") == true
            || model.Contains("Emulator") == true
            || model.Contains("Android SDK built for x86") == true)
        {
            return true;
        }

        if (manufacturer.Contains("Genymotion") == true)
        {
            return true;
        }

        if (brand.Contains("generic") == true && device.Contains("generic") == true)
        {
            return true;
        }

        if (product.Equals("google_sdk") == true || product.Equals("unknown") == true)
        {
            return true;
        }

        if (hardware.Contains("goldfish") == true || hardware.Contains("ranchu") == true)
        {
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------
    // Root detection helpers
    // ---------------------------------------------------------

    private bool CanGetRootBySu(out string reason)
    {
        reason = string.Empty;

        if (TryExecShell("su -c id", out string output) == true)
        {
            if (string.IsNullOrEmpty(output) == false && output.Contains("uid=0") == true)
            {
                reason = "exec:su uid=0";
                return true;
            }
        }

        return false;
    }

    private string GetProp(string key)
    {
        if (string.IsNullOrEmpty(key) == true)
        {
            return string.Empty;
        }

        if (TryExecShell($"getprop {key}", out string output) == false)
        {
            return string.Empty;
        }

        return output.Trim();
    }

    private bool TryExecShell(string command, out string output)
    {
        output = string.Empty;

        try
        {
            using (var process = new AndroidJavaClass("java.lang.Runtime")
                       .CallStatic<AndroidJavaObject>("getRuntime")
                       .Call<AndroidJavaObject>("exec", new string[] { "sh", "-c", command }))
            {
                using (var inputStream = process.Call<AndroidJavaObject>("getInputStream"))
                {
                    output = ReadAllFromJavaInputStream(inputStream);
                }

                // exit code 확인이 필요하면 getErrorStream도 같이 읽는 형태로 확장 가능
                process.Call<int>("waitFor");
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private string ReadAllFromJavaInputStream(AndroidJavaObject inputStream)
    {
        if (inputStream == null)
        {
            return string.Empty;
        }

        try
        {
            using (var reader = new AndroidJavaObject("java.io.BufferedReader",
                       new AndroidJavaObject("java.io.InputStreamReader", inputStream)))
            {
                var sb = new StringBuilder(128);

                while (true)
                {
                    string line = reader.Call<string>("readLine");
                    if (line == null)
                    {
                        break;
                    }

                    sb.AppendLine(line);
                }

                return sb.ToString();
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private string FindInstalledRootPackage()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var pm = activity.Call<AndroidJavaObject>("getPackageManager");

                for (int i = 0; i < SuspectPackages.Length; i++)
                {
                    string pkg = SuspectPackages[i];

                    try
                    {
                        pm.Call<AndroidJavaObject>("getPackageInfo", pkg, 0);
                        return pkg;
                    }
                    catch
                    {
                        // not installed
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    // ---------------------------------------------------------
    // Build helper
    // ---------------------------------------------------------

    private string SafeGetStatic(AndroidJavaClass cls, string fieldName)
    {
        if (cls == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(fieldName) == true)
        {
            return string.Empty;
        }

        try
        {
            string value = cls.GetStatic<string>(fieldName);
            return value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
