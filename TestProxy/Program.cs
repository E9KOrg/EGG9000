using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using EGG9000.Bot.EggIncAPI;

using Google.Protobuf;

using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace TestProxy {
    class Program {
        public static ProxyServer proxyServer;

        public static int OnBeforeTunnelConnectRequest { get; private set; }

        static void Main(string[] args) {
            proxyServer = new ProxyServer();
            proxyServer.BeforeRequest += ProxyServer_BeforeRequest;
            proxyServer.BeforeResponse += ProxyServer_BeforeResponse;

            var explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, 8000, true) {
                // Use self-issued generic certificate on all https requests
                // Optimizes performance by not creating a certificate for each https-enabled domain
                // Useful when certificate trust is not required by proxy clients
                GenericCertificate = new X509Certificate2(@"D:\Websites\EGG9000\TestProxy\proxy.crt", "joshtrek")
            };

            // Fired when a CONNECT request is received
            explicitEndPoint.BeforeTunnelConnectRequest += ExplicitEndPoint_BeforeTunnelConnectRequest;

            // An explicit endpoint is where the client knows about the existence of a proxy
            // So client sends request in a proxy friendly manner
            proxyServer.AddEndPoint(explicitEndPoint);
            proxyServer.Start();


            Console.WriteLine("Proxy server listening on 8000");
            while(true) { }
        }

        private static Task ExplicitEndPoint_BeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e) {
            if(e.HttpClient.Request.RequestUri.Host.Contains("aux")) {
                Console.WriteLine(e.HttpClient.Request.RequestUri.Host);
                e.DecryptSsl = true;
            }
            return Task.CompletedTask;
        }

        private static async System.Threading.Tasks.Task ProxyServer_BeforeResponse(object sender, Titanium.Web.Proxy.EventArguments.SessionEventArgs e) {
            //http://afx-2-dot-auxbrainhome.appspot.com/ei/first_contact_secure
            Console.WriteLine($"BEfore Request: {e.HttpClient.Request.Host} {e.HttpClient.Request.RequestUriString}");
            if(e.HttpClient.Request.Host.Contains("auxbrain") || e.HttpClient.Request.Host.Contains("egg")) {
                Console.WriteLine($"Response: {e.HttpClient.Request.RequestUriString}");
                var responseString = System.Convert.FromBase64String(await e.GetResponseBodyAsString());

                Console.WriteLine();
                Console.WriteLine(await e.GetResponseBodyAsString());
                Console.WriteLine();

            }
        }

        private static async System.Threading.Tasks.Task ProxyServer_BeforeRequest(object sender, Titanium.Web.Proxy.EventArguments.SessionEventArgs e) {
            Console.WriteLine($"Request: {e.HttpClient.Request.Host} {e.HttpClient.Request.RequestUriString}");
            string hostname = e.HttpClient.Request.RequestUri.Host;
            if(hostname.Contains("auxbrain")) {
            }
            if(e.HttpClient.Request.RequestUriString.Contains("auxbrain")) {
                var requestString = System.Convert.FromBase64String((await e.GetRequestBodyAsString()).Substring(5));
                var ms = new MemoryStream();
                ms.Write(requestString);
                ms.Position = 0;

                //var res = Ei.EggIncFirstContactRequestSecure.Parser.ParseFrom(ms);

                //var ms1 = new MemoryStream();
                //res.Request.WriteTo(ms1);
                //ms1.Position = 0;
                //var sr = new StreamReader(ms1);
                //var dataBytes = ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd());

                //var hash = GetHash(dataBytes);

                Console.WriteLine();
                Console.WriteLine(e.HttpClient.Request.RequestUriString);
                Console.WriteLine(await e.GetRequestBodyAsString());
                Console.WriteLine();

                //		Result	"data=CnQQGxgCIhJFSTYyMjkyOTIwNzA5OTM5MjAqEDY1MTI1Y2I1MzViZDcyZjEyADoVMTAyMzcxNjU5Nzc2NDgxNTgwNDI5Qi8KEkVJNjIyOTI5MjA3MDk5MzkyMBAbGgYxLjIwLjMiBjExMTE1MyoHQU5EUk9JRBJANzA2OThiYmExZGFmYzVhZjgwYTBhYzU0NGYzYzMwNWY3NTM1YTU2YzcyM2FiMzg2NTI3MmViMzBmOTU4MjE3Nw=="	string
                //e.SetRequestBodyString("data=CnQQGxgCIhJFSTYyMjkyOTIwNzA5OTM5MjAqEDY1MTI1Y2I1MzViZDcyZjEyADoVMTAyMzcxNjU5Nzc2NDgxNTgwNDI5Qi8KEkVJNjIyOTI5MjA3MDk5MzkyMBAbGgYxLjIwLjMiBjExMTE1MyoHQU5EUk9JRBJANzA2OThiYmExZGFmYzVhZjgwYTBhYzU0NGYzYzMwNWY3NTM1YTU2YzcyM2FiMzg2NTI3MmViMzBmOTU4MjE3Nw==");
            }
        }

        public static string GetHash(byte[] byteArray) {
            SHA256 sha256Hash = SHA256.Create();
            var _magic = 0x3b9af419;
            var _salt = ASCIIEncoding.ASCII.GetBytes(ByteArrayToString(sha256Hash.ComputeHash(ASCIIEncoding.ASCII.GetBytes("***REMOVED***"))));
            byteArray[_magic % byteArray.Length] = 0x1b;
            return ByteArrayToString(sha256Hash.ComputeHash(byteArray.Concat(_salt).ToArray()));
        }

        public static string ByteArrayToString(byte[] ba) {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach(byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
    }
}
