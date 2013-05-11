//
// This source code is licensed in accordance with the licensing outlined
// on the main Tychaia website (www.tychaia.com).  Changes to the
// license on the website apply retroactively.
//
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using System.Net;
using System.Collections.Specialized;
using System.Web.Script.Serialization;
using System.Web.Helpers;

namespace Phabricator.Conduit
{
    public class ConduitClient
    {
        private string m_URI;
        private string m_SessionKey;
        private int? m_ConnectionID;
        /// <summary>
        /// The name of this Conduit client; defaults to "C# conduit client".
        /// </summary>
        /// <value>The name of the client.</value>
        public string ClientName
        {
            get;
            set;
        }
        /// <summary>
        /// The version of this Conduit client; defaults to "1".
        /// </summary>
        /// <value>The client version.</value>
        public string ClientVersion
        {
            get;
            set;
        }
        /// <summary>
        /// The username to perform Conduit request as.
        /// </summary>
        /// <value>The user.</value>
        public string User
        {
            get;
            set;
        }
        /// <summary>
        /// The conduit certificate to use with the server.
        /// </summary>
        /// <value>The certificate.</value>
        public string Certificate
        {
            get;
            set;
        }

        public ConduitClient(string uri)
        {
            this.m_URI = uri;
            this.ClientName = "C# conduit client";
            this.ClientVersion = "1";
        }

        private static string SHA1(string input)
        {
            var algorithm = new SHA1Managed();
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.ASCII.GetBytes(input))).Replace("-", string.Empty);
        }

        private void CreateSession()
        {
            int token = (int)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
            var result = this.Do(
                "conduit.connect",
                new {
                    client = this.ClientName,
                    clientVersion = this.ClientVersion,
                    user = this.User,
                    authToken = token,
                    authSignature = SHA1(token + this.Certificate).ToLowerInvariant()
                });
            this.m_SessionKey = result.sessionKey;
            this.m_ConnectionID = result.connectionID;
        }

        public dynamic Do(string call, object json)
        {
            // Ensure our session key exists.
            // TODO: If we get invalid session 
            if (this.m_SessionKey == null && call != "conduit.connect")
                this.CreateSession();

            // Convert object into dictionary.
            var parameterStore = new Dictionary<string, object>();
            if (json != null)
            {
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(json))
                {
                    parameterStore.Add(descriptor.Name, descriptor.GetValue(json));
                }
            }
            if (this.m_SessionKey != null && this.m_ConnectionID != null)
            {
                parameterStore.Add("__conduit__",
                new Dictionary<string, object>
                {
                    { "sessionKey", this.m_SessionKey },
                    { "connectionID", this.m_ConnectionID }
                });
            }

            // Serialize parameters.
            var serializer = new JavaScriptSerializer();
            var requestParams = serializer.Serialize(parameterStore);

            // Send web request.
            var resultJson = "";
            using (var client = new WebClient())
            {
                var reqparam = new NameValueCollection();
                reqparam.Add("params", requestParams);
                reqparam.Add("output", "json");
                reqparam.Add("__conduit__", "true");
                var bytes = client.UploadValues(this.m_URI + "/" + call, reqparam);
                resultJson = Encoding.ASCII.GetString(bytes);
            }

            dynamic result = Json.Decode(resultJson);
            if (!string.IsNullOrWhiteSpace(result.error_code) ||
                !string.IsNullOrWhiteSpace(result.error_info))
            {
                throw new ConduitException(result.error_code, result.error_info);
            }
            return result.result;
        }
    }
}

