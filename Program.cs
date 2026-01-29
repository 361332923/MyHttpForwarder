using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    static string targetUrlPart = "example.com/api"; 
    static string forwardToUrl = "http://your-server.com/api"; 

    static async Task Main(string[] args)
    {
        Console.WriteLine("--- HTTP 抓取转发工具启动中 ---");

        // 核心修复：使用默认证书引擎，不再依赖外部 Provider
        if (!CertMaker.rootCertExists())
        {
            Console.WriteLine("正在尝试安装根证书...");
            CertMaker.createRootCert();
            CertMaker.trustRootCert();
        }

        // 启动配置：解密 HTTPS 并作为系统代理
        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.DecryptSSL;

        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            if (session.fullUrl.Contains(targetUrlPart) && session.oResponse.MIMEType.Contains("json"))
            {
                string jsonBody = session.GetResponseBodyAsString();
                Console.WriteLine($"[抓取成功] {session.fullUrl}");
                
                Task.Run(async () => {
                    try {
                        using var client = new HttpClient();
                        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        await client.PostAsync(forwardToUrl, content);
                    } catch (Exception ex) {
                        Console.WriteLine("转发异常: " + ex.Message);
                    }
                });
            }
        };

        FiddlerApplication.Startup(8888, flags);
        Console.WriteLine("服务已运行。按任意键退出...");
        Console.ReadLine();
        FiddlerApplication.Shutdown();
    }
}
