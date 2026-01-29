using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    static string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capture_log.txt");

    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法：HttpForwarder.exe <URL正则> <转发地址> [端口(默认8888)]");
            return;
        }

        string urlPattern = args[0];
        string forwardToUrl = args[1];
        int listenPort = (args.Length >= 3) ? int.Parse(args[2]) : 8888;

        WriteLog($"=== 程序启动 (优化版) ===");

        // --- 核心修复 1：强制使用 BouncyCastle 引擎生成 2048 位证书 ---
        // 这是解决 .NET 8 下看不到 HTTPS 的关键配置
        FiddlerApplication.Prefs.SetStringPref("fiddler.certmaker.bc.keypair", "2048");
        FiddlerApplication.Prefs.SetStringPref("fiddler.certmaker.bc.engine", "BouncyCastle");
        
        // === HTTPS 解密必须开关 ===
        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.streaming.abortifclientaborts", true);
        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.streaming.ForgetStreamedData", false);
        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.https.decrypt", true);

        // 允许 MITM
        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.https.ignorecerterrors", true);
        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.https.captureconnect", true);

        // TLS 兼容（Win10 必开）
        FiddlerApplication.Prefs.SetStringPref("fiddler.network.https.SslProtocols", "Tls12");

        // 检查并安装证书
        if (!CertMaker.rootCertExists())
        {
            WriteLog("[!] 初始化 HTTPS 根证书...");
            if (!CertMaker.createRootCert()) 
            {
                WriteLog("[错误] 创建证书失败，HTTPS 将不可用。");
            }
            else
            {
                if (!CertMaker.trustRootCert()) 
                {
                    WriteLog("[错误] 用户拒绝信任证书，HTTPS 将不可用。");
                    // 重要：在 Windows 10 上可能需要手动运行证书信任
                    WriteLog("[提示] 请尝试以管理员身份运行程序，或手动信任证书。");
                }
                else
                {
                    WriteLog("[OK] 证书已成功创建并信任。");
                }
            }
        }
        else
        {
            WriteLog("[OK] 根证书校验通过。");
        }

        // --- 核心修复 2：性能优化 (BeforeRequest) ---
        // 在解密之前就进行过滤！不匹配正则的流量，直接跳过解密，大幅提升网页打开速度。
        FiddlerApplication.BeforeRequest += (session) =>
        {
            // 如果 URL 不匹配我们的正则，就不浪费 CPU 去解密它
            if (!Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
            {
                // 标记为不解密 (Tunnel-Through)
                session["x-no-decrypt"] = "true";
            }
            else
            {
                // 确保 HTTPS 流量被解密
                session["x-decrypt"] = "true";
            }
        };

        // 核心监听逻辑 (AfterSessionComplete)
        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            try
            {
                // 双重检查：确保是我们关心的 URL
                if (Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
                {
                    string mimeType = session.oResponse?.MIMEType ?? "unknown";

                    // 记录探测日志
                    WriteLog($"[探测] {session.fullUrl} ({mimeType})");

                    // 只有 JSON 才转发
                    if (mimeType.Contains("json") && session.oResponse != null)
                    {
                        string jsonBody = session.GetResponseBodyAsString();
                        WriteLog($" >> [命中目标] 正在转发...");

                        Task.Run(async () => {
                            try {
                                using (var client = new HttpClient())
                                {
                                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                                    var response = await client.PostAsync(forwardToUrl, content);
                                    WriteLog($" >> [转发成功] {response.StatusCode}");
                                }
                            } catch (Exception ex) {
                                WriteLog($" >> [转发失败] {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[异常] {ex.Message}");
            }
            finally
            {
                // 内存清理
                session.RequestBody = null;
                session.ResponseBody = null;
            }
        };

        // 添加错误处理器 - 帮助诊断HTTPS问题
        FiddlerApplication.OnNotification += (sender, e) =>
        {
            WriteLog($"[通知] {e.NotifyString}");
        };

        FiddlerApplication.OnWebSocketMessage += (sender, e) =>
        {
            WriteLog($"[WebSocket] {e.wsMessage.PayloadAsString()}");
        };

        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.AllowRemoteClients
                                        | FiddlerCoreStartupFlags.DecryptSSL; // 必须开启 SSL 解密

        FiddlerApplication.Startup(listenPort, flags);
        WriteLog($"[*] 监听服务运行中 (端口: {listenPort})...");

        Console.WriteLine("按回车键停止程序...");
        Console.ReadLine();
        FiddlerApplication.Shutdown();
    }

    static void WriteLog(string message)
    {
        try
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            Console.Write(entry);
            File.AppendAllText(logFilePath, entry);
        }
        catch { }
    }
}
