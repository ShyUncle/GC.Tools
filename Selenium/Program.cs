using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Chrome;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Support;
namespace Selenium
{
    internal class Program
    {
        static IWebDriver driver;
        static void Main(string[] args)
        {
            try
            {
                CreateDriver();
                bool goon = true;
                while (goon)
                {
                    GotoOperationPage();
                    var txt = Console.ReadLine();
                    if (txt == "exit")
                    {
                        goon = false;
                        Console.WriteLine("结束循环");
                    }
                }
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // Always call `quit` to ensure your session cleans up properly and you're not charged for unused time
                driver.Quit();
            }
        }
        static void CreateDriver()
        {
            ChromeOptions options = new ChromeOptions();

            // Set launch args similar to puppeteer's for best performance
            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-breakpad");
            options.AddArgument("--disable-component-extensions-with-background-pages");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-features=TranslateUI,BlinkGenPropertyTrees");
            options.AddArgument("--disable-ipc-flooding-protection");
            options.AddArgument("--disable-renderer-backgrounding");
            options.AddArgument("--enable-features=NetworkService,NetworkServiceInProcess");
            options.AddArgument("--force-color-profile=srgb");
            options.AddArgument("--hide-scrollbars");
            options.AddArgument("--metrics-recording-only");
            options.AddArgument("--mute-audio");
            //  options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");

            // Note we set our token here, with `true` as a third arg
            // options.AddAdditionalOption("browserless:token", "YOUR-API-TOKEN");

            driver = new ChromeDriver(System.AppDomain.CurrentDomain.BaseDirectory.ToString(), options);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
        }
        static void GotoOperationPage()
        {
            driver.Navigate().GoToUrl("https://open.work.weixin.qq.com/wwopen/developer#/sass/customApp/deploy/detail?auditorderid=au20211227102640341");
            Thread.Sleep(1000);
            var loginPage = GetLoginIframeElement();
            if (loginPage != null)
            {
                Console.WriteLine("检测到登录页");
                driver.SwitchTo().Frame(loginPage);

                Console.WriteLine(driver.Title);
                Console.WriteLine("查找登录二维码");
                var logPic = FindElement("//img[@class=\"qrcode_login_img js_qrcode_img\"]");
                if (logPic != null)
                {
                    Console.WriteLine("查找到登录二维码");
                    Screenshot screenshot = ((ITakesScreenshot)logPic).GetScreenshot();
                    screenshot.ToString();
                    screenshot.SaveAsFile("qrcode.png", ScreenshotImageFormat.Png);
                    Console.WriteLine("已截图，等待管理员扫码");
                }
                while (logPic != null)
                {
                    Thread.Sleep(10000);
                    var refreshBtn = FindElement("//a[@class=\"qrcode_login_reload\"]");
                    if (refreshBtn != null && refreshBtn.Displayed)
                    {
                        refreshBtn.Click();
                        Screenshot screenshot = ((ITakesScreenshot)logPic).GetScreenshot();
                        screenshot.SaveAsFile("qrcode.png", ScreenshotImageFormat.Png);
                        Console.WriteLine("二维码过期重新截图，等待管理员扫码");
                    }
                    logPic = FindElement("//img[@class=\"qrcode_login_img js_qrcode_img\"]");
                }

                Console.WriteLine("扫码完成");
                Thread.Sleep(100);
                driver.SwitchTo().DefaultContent();
                var corpList = FindElements("//li[@class=\"login_selectBiz_item\"]");
                foreach (var corp in corpList)
                {
                    var corpName = corp.FindElement(By.XPath("//div[@class=\"login_selectBiz_item_name\"]"))?.Text;
                    if (corpName != null && corpName.Trim() == "河南天英信息技术有限公司")
                    {
                        corp.Click();

                        Console.WriteLine("选择公司完毕");
                        break;
                    }
                }
                Thread.Sleep(5000);
            }
            Console.WriteLine("进入操作页");
            driver.Navigate().GoToUrl("https://open.work.weixin.qq.com/wwopen/developer#/sass/customApp/tpl/info?id=1011292");


        }

        static IWebElement FindElement(string xpath)
        {
            try
            {
                return driver.FindElement(By.XPath(xpath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
        }

        static ReadOnlyCollection<IWebElement> FindElements(string xpath)
        {
            try
            {
                return driver.FindElements(By.XPath(xpath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new ReadOnlyCollection<IWebElement>(new List<IWebElement>());
            }
        }

        static IWebElement GetLoginIframeElement()
        {
            try
            {
                return driver.FindElement(By.TagName("iframe"));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Wait for the expected condition is satisfied, return immediately
        /// </summary>
        /// <param name="expectedCondition"></param>
        public void WaitForPage(string title)
        {
            WebDriverWait _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            _wait.Until((d) => { return d.Title.ToLower().StartsWith(title.ToLower()); });
            //to do
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="we"></param>
        public void WaitForElement(string id)
        {
            WebDriverWait _wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            _wait.Until(c => driver.FindElement(By.Id(id)));
        }
    }
}
