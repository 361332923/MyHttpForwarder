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

        WriteLog($"=== 程序启动 ===");

        // --- 核心修复：强制证书检查 ---
        if (!CertMaker.rootCertExists())
        {
            WriteLog("[!] 未发现根证书，正在尝试创建并安装...");
            if (!CertMaker.createRootCert())
            {
                WriteLog("[错误] 创建根证书失败。");
            }
            if (!CertMaker.trustRootCert())
            {
                WriteLog("[错误] 用户拒绝信任根证书，HTTPS 抓取将失效。");
            }
        }
        else
        {
            WriteLog("[OK] 根证书已存在。");
        }

        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            try
            {
                // 1. 只要 URL 匹配正则，就记录到本地日志（方便排查）
                if (Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
                {
                    string mimeType = session.oResponse?.MIMEType ?? "unknown";
                    
                    // 记录所有匹配到的请求到日志，不论是否为 JSON
                    WriteLog($"[探测] URL: {session.fullUrl} | 类型: {mimeType} | 状态码: {session.responseCode}");

                    // 2. 只有满足 JSON 条件才进行 POST 转发
                    if (mimeType.Contains("json") && session.oResponse != null)
                    {
                        string jsonBody = session.GetResponseBodyAsString();
                        WriteLog($" >> [命中目标] 准备转发 JSON 数据...");

                        Task.Run(async () => {
                            try {
                                using (var client = new HttpClient())
                                {
                                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                                    var response = await client.PostAsync(forwardToUrl, content);
                                    WriteLog($" >> [转发结果] {response.StatusCode}");
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
                session.RequestBody = new byte[0];
                session.ResponseBody = new byte[0];
            }
        };

        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.DecryptSSL;

        FiddlerApplication.Startup(listenPort, flags);
        WriteLog($"[*] 监听中... (端口: {listenPort})");

        while (true)
        {
            await Task.Delay(60000);
            // 日志自动清理逻辑保持不变...
        }
    }

    static void WriteLog(string message)
    {
        try
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            Console.Write(entry);
            File.AppendAllText(logFilePath, entry);
        }
        catch { }
    }
}
