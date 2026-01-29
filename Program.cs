using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    // 配置：需要抓取的 URL 关键字
    static string targetUrlPart = "example.com/api";
    // 配置：转发的目标 HTTP 地址
    static string forwardToUrl = "http://your-server.com/receive";

    static async Task Main(string[] args)
    {
        Console.WriteLine("--- HTTP 抓取转发工具启动中 ---");

        // 1. 证书处理逻辑
        if (!CertMaker.rootCertExists())
        {
            Console.WriteLine("正在请求安装根证书，请在弹窗中选择‘是’...");
            if (!CertMaker.createRootCert() || !CertMaker.trustRootCert())
            {
                Console.WriteLine("错误：证书安装失败，HTTPS 抓取可能无法工作。");
            }
        }

        // 2. 配置 FiddlerCore 启动参数
        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default | FiddlerCoreStartupFlags.AllowRemoteClients;
        
        // 3. 注册事件：抓取返回包
        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            // 过滤 URL
            if (session.fullUrl.Contains(targetUrlPart))
            {
                // 只处理 JSON 内容
                if (session.oResponse.MIMEType.Contains("json"))
                {
                    string jsonBody = session.GetResponseBodyAsString();
                    Console.WriteLine($"[发现目标] 抓取到 URL: {session.fullUrl}");
                    
                    // 异步转发，不阻塞抓包主流程
                    Task.Run(async () => {
                        try {
                            using var client = new HttpClient();
                            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                            await client.PostAsync(forwardToUrl, content);
                            Console.WriteLine(" >> 转发成功！");
                        } catch (Exception ex) {
                            Console.WriteLine(" >> 转发失败: " + ex.Message);
                        }
                    });
                }
            }
        };

        // 4. 启动代理 (监听 8888 端口)
        FiddlerApplication.Startup(8888, flags);
        Console.WriteLine("服务已运行，按任意键退出...");
        Console.ReadKey();

        // 退出时清理系统代理
        FiddlerApplication.Shutdown();
    }
}
