﻿using Elasticsearch.Net;
using Microsoft.Extensions.Options;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ElasticSearchDemo
{
    public interface IElasticClientProvider
    {
        IElasticClient GetElasticClient();
    }
    public class ElasticClientProvider : IElasticClientProvider
    {
        private readonly IOptions<ElasticSearchOptions> _option;

        private IElasticClient _clientPerson;
        public ElasticClientProvider(IOptions<ElasticSearchOptions> option)
        {
            _option = option;
        }

        public IElasticClient GetElasticClient()
        {
            if (_clientPerson != null)
            {
                return _clientPerson;
            }
            var config = _option.Value;
            var nodeList = new List<Node>();
            foreach (var con in config.ConnectionString.Split("|").ToList())
            {
                var node = new Uri(con);
                nodeList.Add(node);
            }
            var settings1 = new ConnectionSettings(new StaticConnectionPool(nodeList)).DefaultIndex("person");
            settings1.BasicAuthentication(config.AuthUserName, config.AuthPassWord);
            settings1.EnableHttpCompression(true);
            //settings1.ServerCertificateValidationCallback(CertificateValidations.AllowAll);
            _clientPerson = new ElasticClient(settings1);
            return _clientPerson;
        }
    }
}
