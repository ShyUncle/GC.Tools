using IdentityModel.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace IdentityClient
{
    class Program
    {
        static void Main(string[] args)
        {
            IdentityTest();
            Console.ReadLine();
        }

        // discover endpoints from metadata
        static async Task IdentityTest()
        {
            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync(new DiscoveryDocumentRequest()
            {
                Address = "http://192.168.1.156:5000",
                Policy = new DiscoveryPolicy() { RequireHttps = false }
            });
            if (disco.IsError)
            {
                Console.WriteLine(disco.Error);
                return;
            }
            var tokenResponse = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = disco.TokenEndpoint,

                ClientId = "client",
                ClientSecret = "511536EF-F270-4058-80CA-1C89C192F69A",
                Scope = "api1",

            });
            var user = new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,

                ClientId = "p",
                ClientSecret = "511536EF-F270-4058-80CA-1C89C192F69A",
                Scope = "api1",
                UserName = "a",
                Password = "b"
            };
            tokenResponse = await client.RequestPasswordTokenAsync(user);

            if (tokenResponse.IsError)
            {
                Console.WriteLine(tokenResponse.Error);
                return;
            }

            Console.WriteLine(tokenResponse.Json);

            client = new HttpClient();
            client.SetBearerToken(tokenResponse.AccessToken);

            var response = await client.GetAsync("http://localhost:5001/api/identity");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(response.StatusCode);
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine(JArray.Parse(content));
            }
        }
    }
}
