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

                GotoOperationPage();
                var txt = Console.ReadLine();

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
            driver.Navigate().GoToUrl("https://www.baidu.com");
            Thread.Sleep(1000);
            var kewordInput = FindElement("//input[@id=\"kw\"]");
            if (kewordInput != null)
            {
                Console.WriteLine("找到文本框");
                kewordInput.Click();
                kewordInput.SendKeys("嗨");
                Thread.Sleep(100);
                var searchBtn = FindElement("//input[@id=\"su\"]");
                if (searchBtn != null)
                {
                    searchBtn.Click();
                }
            }
            Thread.Sleep(1000);
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
