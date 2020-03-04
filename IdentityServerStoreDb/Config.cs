// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityServer4.Models;
using System.Collections.Generic;

namespace IdentityServerStoreDb
{
    public static class Config
    {
        public static IEnumerable<IdentityResource> Ids =>
            new IdentityResource[]
            {
                new IdentityResources.OpenId(),
                new IdentityResources.Profile(),
            };


        public static IEnumerable<ApiResource> Apis =>
            new ApiResource[]
            {
                new ApiResource("api1", "My API #1")
            };


        public static IEnumerable<Client> Clients =>
            new Client[]
            {
                // client credentials flow client
                new Client
                { 
                    ClientId = "client",
                    ClientName = "Client Credentials Client",

                    AllowedGrantTypes = GrantTypes.ClientCredentials,
                    ClientSecrets = { new Secret("511536EF-F270-4058-80CA-1C89C192F69A".Sha256()) },

                    AllowedScopes = { "api1" }
                },
                new Client{
                    ClientId="p",
                    ClientSecrets = { new Secret("511536EF-F270-4058-80CA-1C89C192F69A".Sha256()) },
                    ClientName="password",
                    AllowedGrantTypes=GrantTypes.ResourceOwnerPassword,
                    AllowedScopes={"api1" }
                },
                // MVC client using code flow + pkce
                new Client
                {
                    ClientId = "mvc",
                    ClientName = "MVC Client",
                    AllowedGrantTypes = GrantTypes.Code,
                    RequirePkce = true,
                    ClientSecrets = { new Secret("49C1A7E1-0C79-4A89-A3D6-A37998FB86B0".Sha256()) },
                     RequireConsent=false,//是否显示授权页面
                    RedirectUris = { "http://192.168.1.156:5002/signin-oidc" },
                    FrontChannelLogoutUri = "http://192.168.1.156:5002/signout-oidc",
                    PostLogoutRedirectUris = { "http://192.168.1.156:5002/signout-callback-oidc" },

                    AllowOfflineAccess = true,
                    AllowedScopes = { "openid", "profile", "api1" }
                },

                // SPA client using code flow + pkce
                new Client
                {
                    ClientId = "spa",
                    ClientName = "SPA Client",
                    ClientUri = "http://192.168.1.156:5003",
                    RequireConsent=false,//是否显示授权页面
                    AllowedGrantTypes = GrantTypes.Code,
                  AllowAccessTokensViaBrowser=true,
                  //  ClientSecrets={ new Secret("1" .Sha256())},
                    RequirePkce = true,
                    RequireClientSecret = false,
                    RedirectUris =
                    {
                        "http://192.168.1.156:5003/index.html",
                        "http://192.168.1.156:5003/callback.html",
                        "http://192.168.1.156:5003/silent.html",
                        "http://192.168.1.156:5003/popup.html",
                    },

                    PostLogoutRedirectUris = { "http://192.168.1.156:5003/index.html" },
                    AllowedCorsOrigins = { "http://192.168.1.156:5003" },
                    AllowedScopes = { "openid", "profile", "api1" }
                }
            };
    }
}
