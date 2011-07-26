﻿using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace NuGet {
    public class DataServiceContextWrapper : IDataServiceContext {
        private static readonly MethodInfo _executeMethodInfo = typeof(DataServiceContext).GetMethod("Execute", new[] { typeof(Uri) });
        private readonly DataServiceContext _context;
        private readonly HashSet<string> _methodNames;

        public DataServiceContextWrapper(Uri serviceRoot) {
            if (serviceRoot == null) {
                throw new ArgumentNullException("serviceRoot");
            }
            _context = new DataServiceContext(serviceRoot);
            _context.MergeOption = MergeOption.OverwriteChanges;
            _methodNames = new HashSet<string>(GetSupportedMethodNames(), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetSupportedMethodNames() {
            Uri metadataUri = _context.GetMetadataUri();

            if (metadataUri == null) {
                return Enumerable.Empty<string>();
            }

            // Make a request to the metadata uri and get the schema
            var client = new HttpClient(metadataUri);
            string schema = Encoding.UTF8.GetString(client.DownloadData());

            return ExtractMethodNamesFromSchema(schema);
        }

        internal static IEnumerable<string> ExtractMethodNamesFromSchema(string schema) {
            var schemaDocument = XDocument.Parse(schema);

            // Get all entity containers
            var entityContainers = from e in schemaDocument.Descendants()
                                   where e.Name.LocalName == "EntityContainer"
                                   select e;

            // Find the entity container with the Packages entity set
            var packageEntityContainer = (from e in entityContainers
                                          let entitySet = e.Elements().FirstOrDefault(el => el.Name.LocalName == "EntitySet")
                                          let name = entitySet != null ? entitySet.Attribute("Name").Value : null
                                          where name != null && name.Equals("Packages", StringComparison.OrdinalIgnoreCase)
                                          select e).FirstOrDefault();

            // Get all functions
            return from e in packageEntityContainer.Elements()
                   where e.Name.LocalName == "FunctionImport"
                   select e.Attribute("Name").Value;
        }

        public Uri BaseUri {
            get {
                return _context.BaseUri;
            }
        }

        public event EventHandler<SendingRequestEventArgs> SendingRequest {
            add {
                _context.SendingRequest += value;
            }
            remove {
                _context.SendingRequest -= value;
            }
        }

        public event EventHandler<ReadingWritingEntityEventArgs> ReadingEntity {
            add {
                _context.ReadingEntity += value;
            }
            remove {
                _context.ReadingEntity -= value;
            }
        }

        public bool IgnoreMissingProperties {
            get {
                return _context.IgnoreMissingProperties;
            }
            set {
                _context.IgnoreMissingProperties = value;
            }
        }

        public IDataServiceQuery<T> CreateQuery<T>(string entitySetName, IDictionary<string, object> queryOptions) {
            var query = _context.CreateQuery<T>(entitySetName);
            foreach (var pair in queryOptions) {
                query = query.AddQueryOption(pair.Key, pair.Value);
            }
            return new DataServiceQueryWrapper<T>(this, query);
        }

        public IDataServiceQuery<T> CreateQuery<T>(string entitySetName) {
            return new DataServiceQueryWrapper<T>(this, _context.CreateQuery<T>(entitySetName));
        }

        public IEnumerable<T> Execute<T>(Type elementType, DataServiceQueryContinuation continuation) {
            // Get the generic execute method method
            MethodInfo executeMethod = _executeMethodInfo.MakeGenericMethod(elementType);

            // Get the results from the continuation
            return (IEnumerable<T>)executeMethod.Invoke(_context, new object[] { continuation.NextLinkUri });
        }

        public IEnumerable<T> ExecuteBatch<T>(DataServiceRequest request) {
            return _context.ExecuteBatch(request)
                           .Cast<QueryOperationResponse>()
                           .SelectMany(o => o.Cast<T>());
        }


        public Uri GetReadStreamUri(object entity) {
            return _context.GetReadStreamUri(entity);
        }

        public bool SupportsServiceMethod(string methodName) {
            return _methodNames.Contains(methodName);
        }
    }
}
