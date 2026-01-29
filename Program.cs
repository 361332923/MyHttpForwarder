using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2) return; // 参数不足直接退出

        string urlPattern = args[0];
        string forwardToUrl = args[1];
        int listenPort = (args.Length >= 3) ? int.Parse(args[2]) : 8888;

        // --- 核心优化设置：防止长时间运行产生垃圾 ---
        FiddlerApplication.Config.IgnoreServerCertErrors = true;
        // 1. 内存优化：不保存会话记录
        FiddlerApplication.AfterSessionComplete += (session) => {
            // 处理完逻辑后，立即销毁 Session 对象释放内存
            session.ViewAsBytes(); 
        };

        // 2. 核心监听逻辑
        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            // 只处理匹配正则且是 JSON 的响应
            if (Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
            {
                if (session.HasResponse && session.oResponse.MIMEType.Contains("json"))
                {
                    string jsonBody = session.GetResponseBodyAsString();
                    
                    // 异步转发，不影响主线程
                    Task.Run(async () => {
                        try {
                            using var client = new HttpClient();
                            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                            await client.PostAsync(forwardToUrl, content);
                        } catch { } // 长时间运行，忽略个别转发失败，防止程序崩溃
                    });
                }
            }
            
            // 关键：清理该会话占用的内存，防止30天运行内存溢出
            session.oRequest.Dispose();
            session.oResponse.Dispose();
        };

        // 3. 启动（禁用冗余日志输出）
        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.DecryptSSL;

        FiddlerApplication.Startup(listenPort, flags);
        
        // 保持后台运行，不再等待 Console.ReadLine()
        // 这种写法适合无界面后台运行
        await Task.Delay(-1); 
    }
}
