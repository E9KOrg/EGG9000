
using Google.Protobuf;

using Microsoft.Extensions.Caching.Memory;


using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using ComponentAce.Compression.Libs.zlib;
using Ei;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text.Unicode;
using EGG9000.Common.Database.Entities;
using System.Collections.Generic;

//using static EGG9000.Bot.Automated.LeaderboardUpdater;

namespace EGG9000.Bot.EggIncAPI {


    public class ContractsAPI {
        //public const string BaseAddressNew = "https://ctx-dot-auxbrainhome.appspot.com/";
        public const string BaseAddressNew = "https://www.auxbrain.com/";
        //static string BaseAddressOld = "http://afx-2-dot-auxbrainhome.appspot.com/";

        public const string UserId = "EI5223299518300160";
        
        public const uint ClientVersion = 59;

        public static Ei.BasicRequestInfo GetInfo(string UserId, bool noUserID = false) {
            var info = new Ei.BasicRequestInfo {
                ClientVersion = ClientVersion,
                Version = "1.27.6",
                Build = "1.27.6.0",
                Platform = "IOS",
                Country = "US",
                Language = "en", 
                Debug = false
            };
            if(!noUserID) {
                info.EiUserId = UserId;
            }
            return info;
        }

        public static async Task<bool> Send<T2>(T2 data, string UserId) where T2 : Google.Protobuf.IMessage {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);
                    var outStream = new MemoryStream();
                    var outCodedStream = new CodedOutputStream(outStream);
                    data.WriteTo(outCodedStream);
                    outCodedStream.Flush();
                    outStream.Position = 0;
                    var sr = new StreamReader(outStream);
                    var str = sr.ReadToEnd();
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(str));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");


                    var url = "";

                    switch(data) {
                        case Ei.GiftPlayerCoopRequest d:
                            url = "ei/gift_player_coop";
                            d.Rinfo = GetInfo(UserId);
                            break;
                        case Ei.KickPlayerCoopRequest d:
                            url = "ei/kick_player_coop";
                            d.Rinfo = GetInfo(UserId);
                            break;
                        case Ei.LeaveCoopRequest d:
                            d.Rinfo = GetInfo(UserId);
                            url = "ei/leave_coop";
                            break;
                        default:
                            throw new Exception($"Missing Info for {typeof(T2).Name}");
                    }

                    var response = await client.PostAsync(url, bac);

                    if(response.IsSuccessStatusCode) {
                        var r = await response.Content.ReadAsStringAsync();

                        return true;
                    } else {
                        return false;
                    }
                }
            } catch(Exception) {
                return false;
            }
        }

        public static async Task<UserSubscriptionInfo> GetUserSubscription(string UserId) {
            var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
            using(var client = new HttpClient(handler)) {
                client.BaseAddress = new Uri(BaseAddressNew);
                client.DefaultRequestHeaders.Add("User-Agent", "egginc/1.26.1.3 CFNetwork/1335.0.3 Darwin/21.6.0");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");

                var response = await client.PostAsync($"ei_srv/subscription_status/{UserId}", null);

                if(response.IsSuccessStatusCode) {
                    var r = await response.Content.ReadAsStringAsync();
                    var responseString = System.Convert.FromBase64String(r);

                        return GetFromAuthenticatedMessage<UserSubscriptionInfo>(responseString);
                } else {
                    return default(UserSubscriptionInfo);
                }
            }
        }

        public static async Task<TResponse> Post<TResponse, TRequest>(TRequest data, string UserId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : Google.Protobuf.IMessage {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);
                    //var ms1 = new MemoryStream();
                    //var outCodedStream = new CodedOutputStream(ms1);

                    var url = "";
                    var base64 = "";
                    switch(data) {
                        case Ei.JoinCoopRequest e: {
                                url = "ei/join_coop";
                                e.Rinfo = GetInfo(UserId);
                                //e.WriteTo(ms1);
                                base64 = e.ToByteString().ToBase64();
                                break;
                            }
                        case Ei.GetPeriodicalsRequest e:
                            url = "ei/get_periodicals";
                            e.Rinfo = GetInfo(UserId, true);
                            //e.WriteTo(ms1);
                            base64 = e.ToByteString().ToBase64();
                            break;
                        case Ei.CreateCoopRequest e: {
                                url = "ei/create_coop";
                                e.Rinfo = GetInfo(UserId);

                                //var memorySteam = new MemoryStream();
                                //e.WriteTo(memorySteam);
                                //memorySteam.Position = 0;
                                //var messageData = memorySteam.ToArray();
                                //var message = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData) };
                                //message.WriteTo(ms1);
                                //e.WriteTo(ms1);
                                base64 = e.ToByteString().ToBase64();
                                var b64 = e.ToByteString().ToBase64();
                                break;
                            }
                        case Ei.QueryCoopRequest e:
                            url = "ei/query_coop";
                            e.Rinfo = GetInfo(UserId);
                            //e.WriteTo(ms1);
                            base64 = e.ToByteString().ToBase64();
                            break;
                        case Ei.UpdateCoopPermissionsRequest e:
                            url = "ei/update_coop_permissions";
                            e.Rinfo = GetInfo(UserId);
                            //e.WriteTo(ms1);
                            base64 = e.ToByteString().ToBase64();
                            break;
                        case Ei.ContractCoopStatusUpdateRequest e:
                            url = "ei/update_coop_status";
                            e.Rinfo = GetInfo(UserId);
                            base64 = e.ToByteString().ToBase64();
                            break;
                        case Ei.ConfigRequest e:
                            url = "ei/get_config";
                            e.Rinfo = GetInfo(UserId);
                            base64 = e.ToByteString().ToBase64();
                            break;
                        case Ei.BasicRequestInfo e: {
                                if(typeof(TResponse) == typeof(ContractPlayerInfo)) {
                                    url = "ei_ctx/get_contract_player_info";
                                    var memorySteam = new MemoryStream();
                                    e = GetInfo(UserId);
                                    e.WriteTo(memorySteam);
                                    memorySteam.Position = 0;
                                    var messageData = memorySteam.ToArray();
                                    var message = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                                    base64 = message.ToByteString().ToBase64();
                                    authenticated = true;
                                } else if(typeof(TResponse) == typeof(MyContracts)) {
                                    url = "ei_ctx/get_contracts_archive";
                                    e = GetInfo(UserId);
                                    base64 = e.ToByteString().ToBase64();
                                    authenticated = true;
                                }
                                break;
                            }
                        default:
                            throw new Exception($"Missing Info for {typeof(TRequest).Name}");
                    }
                    var json = JsonConvert.SerializeObject(data);
                    //ms1.Position = 0;
                    //var sr = new StreamReader(ms1);
                    //var bytes = ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd());
                    //var base64 = Convert.ToBase64String(bytes);
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "egginc/1.26.1.3 CFNetwork/1335.0.3 Darwin/21.6.0");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");



                    var response = await client.PostAsync(url, bac);

                    if(response.IsSuccessStatusCode) {
                        var r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(r);

                        if(authenticated) {
                            return GetFromAuthenticatedMessage<TResponse>(responseString);
                        } else {
                            var parse = new MessageParser<TResponse>(() => new TResponse());
                            return parse.ParseFrom(responseString);
                        }
                    } else {
                        return default(TResponse);
                    }
                }
            } catch(Exception e) {
                return default(TResponse);
            }
        }

        public static async Task<Ei.PeriodicalsResponse> GetPeriodicalsAsync() {
            return await ContractsAPI.Post<Ei.PeriodicalsResponse, Ei.GetPeriodicalsRequest>(new Ei.GetPeriodicalsRequest {
                UserId = "EI4765194876354560",
                PiggyFull = false,
                PiggyFoundFull = false,
                SecondsFullRealtime = 2339576.17448521,
                SecondsFullGametime = 391564.659540082,
                SoulEggs = 570149167.28294,
                CurrentClientVersion = ClientVersion,
                Debug = false, 
            }, "EI4765194876354560", true);
        }

        public static async Task<Ei.ContractCoopStatusResponse> GetCoopStatus(string ContractName, string CoopName, CancellationToken cancellationToken = default, string EIID = null, List<UserCoopXref> xrefs = null) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    var model = new Ei.ContractCoopStatusRequest {
                        ContractIdentifier = ContractName,
                        CoopIdentifier = CoopName.ToLower(),
                        Rinfo = GetInfo(EIID ?? UserId),
                        UserId = EIID ?? UserId,
                        ClientVersion = ContractsAPI.ClientVersion
                    };
                    model.WriteTo(ms1);
                    ms1.Position = 0;
                    var sr = new StreamReader(ms1);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                    var response = await client.PostAsync("ei/coop_status", bac, cancellationToken);

                    if(response.IsSuccessStatusCode) {
                        var r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());


                        var coopStatus = GetFromAuthenticatedMessage<Ei.ContractCoopStatusResponse>(responseString);
                        coopStatus.Success = true;
                        if(xrefs != null && coopStatus.Participants.Any(x => x.UserName == "[departed]")) {
                            var departed = coopStatus.Participants.First(x => x.UserName == "[departed]");
                            var matchingUser = xrefs.Where(x => !string.IsNullOrEmpty(x.Status)).Select(x => JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(x.Status)).FirstOrDefault(x => x.ContributionAmount == departed.ContributionAmount);
                            if(matchingUser is not null) {
                                departed.UserName = matchingUser.UserName;
                            }
                        }
                        return coopStatus;
                    } else {
                        return null;
                    }
                }

            } catch(Exception) {
                return null;
            }
        }

        public static T GetFromAuthenticatedMessage<T>(byte[] authenticatedMessage) where T : IMessage, new() {
            var authMessageDecoded = Ei.AuthenticatedMessage.Parser.ParseFrom(authenticatedMessage);

            T message = new T();
            if(authMessageDecoded.Compressed) {
                using(MemoryStream outMemoryStream = new MemoryStream())
                using(ZOutputStream outZStream = new ZOutputStream(outMemoryStream))
                using(Stream inMemoryStream = new MemoryStream(authMessageDecoded.Message.ToArray())) {
                    CopyStream(inMemoryStream, outZStream);
                    outZStream.finish();
                    message.MergeFrom(outMemoryStream.ToArray());
                }
            } else {
                message.MergeFrom(authMessageDecoded.Message);
            }
            return message;
        }
        public static T GetFromAuthenticatedMessage<T>(string authenticatedMessage) where T : IMessage, new() {

            var authMessageDecoded = Ei.AuthenticatedMessage.Parser.ParseFrom(Convert.FromBase64String(authenticatedMessage));

            T message = new T();
            if(authMessageDecoded.Compressed) {
                using(MemoryStream outMemoryStream = new MemoryStream())
                using(ZOutputStream outZStream = new ZOutputStream(outMemoryStream))
                using(Stream inMemoryStream = new MemoryStream(authMessageDecoded.Message.ToArray())) {
                    CopyStream(inMemoryStream, outZStream);
                    outZStream.finish();
                    message.MergeFrom(outMemoryStream.ToArray());
                }
            } else {
                message.MergeFrom(authMessageDecoded.Message);
            }
            return message;
        }


        private static void CopyStream(System.IO.Stream input, System.IO.Stream output) {
            byte[] buffer = new byte[2000];
            int len;
            while((len = input.Read(buffer, 0, 2000)) > 0) {
                output.Write(buffer, 0, len);
            }
            output.Flush();
        }

        public static string GetHash(byte[] byteArray) {
            SHA256 sha256Hash = SHA256.Create();
            var _magic = 0x3b9af419;
            var _salt = ASCIIEncoding.ASCII.GetBytes(ByteArrayToString(sha256Hash.ComputeHash(ASCIIEncoding.ASCII.GetBytes("THE SECRETS OF THE UNIVERSE WILL BE UNLOCKED"))));
            byteArray[_magic % byteArray.Length] = 0x1b;
            return ByteArrayToString(sha256Hash.ComputeHash(byteArray.Concat(_salt).ToArray()));
        }

        public static string ByteArrayToString(byte[] ba) {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach(byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }


        public static async Task<Ei.EggIncFirstContactResponse> FirstContact(string UserId) {
            if(UserId.StartsWith("EI"))
                return await FirstContactNew(UserId);
            //return new Ei.EggIncFirstContactResponse { Success = false, Error = "Error old ID" };
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        ClientVersion = ClientVersion,
                        Platform = Aux.Platform.Droid,
                        UserId = UserId
                    }.WriteTo(ms1);
                    //Serializer.Serialize<FirstContactRequestProto>(ms1, new FirstContactRequestProto { UserId = UserId, P2 = 0, P3 = 2 });
                    ms1.Position = 0;
                    var messageData = ms1.ToArray();
                    var ms2 = new MemoryStream();
                    new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) }.WriteTo(ms2);

                    ms2.Position = 0;
                    var sr = new StreamReader(ms2);

                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));


                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));



                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                    HttpResponseMessage response;

                    //try {
                    response = await client.PostAsync("ei/first_contact_secure", bac);
                    //} catch(Exception) {
                    //await Task.Delay(500);
                    //response = await client.PostAsync("ei/first_contact", bac);
                    //}


                    string r;
                    if(response.IsSuccessStatusCode) {
                        r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());

                        var ms = new MemoryStream();
                        ms.Write(responseString);
                        ms.Position = 0;



                        var backup = Ei.EggIncFirstContactResponse.Parser.ParseFrom(ms);

                        //var coop = Serializer.Deserialize<Ei.EggIncFirstContactResponse>(ms);
                        backup.Success = true;
                        return backup;
                    } else {
                        return new Ei.EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                    }
                }

            } catch(Exception e) {
                return new Ei.EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }
        public static async Task<string> FirstContactRaw(string UserId) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        ClientVersion = ClientVersion,
                        Platform = Aux.Platform.Droid,
                        EiUserId = UserId,
                        DeviceId = UserId,
                        Username = "",
                        //GameServicesId = "102371659776481580429", 
                        Rinfo = GetInfo(UserId)
                    }.WriteTo(ms1);
                    //Serializer.Serialize<FirstContactRequestProto>(ms1, new FirstContactRequestProto { UserId = UserId, P2 = 0, P3 = 2 });
                    ms1.Position = 0;
                    var messageData = ms1.ToArray();
                    var ms2 = new MemoryStream();
                    new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) }.WriteTo(ms2);

                    ms2.Position = 0;
                    var sr = new StreamReader(ms2);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                    HttpResponseMessage response;

                    //try {
                    response = await client.PostAsync("ei/first_contact_secure", bac);
                    //} catch(Exception) {
                    //    await Task.Delay(500);
                    //    response = await client.PostAsync("ei/first_contact", bac);
                    //}

                    if(response.IsSuccessStatusCode) {
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());
                        var authMessageDecoded = Ei.AuthenticatedMessage.Parser.ParseFrom(responseString);

                        if(authMessageDecoded.Compressed) {
                            using(MemoryStream outMemoryStream = new MemoryStream())
                            using(ZOutputStream outZStream = new ZOutputStream(outMemoryStream))
                            using(Stream inMemoryStream = new MemoryStream(authMessageDecoded.Message.ToArray())) {
                                CopyStream(inMemoryStream, outZStream);
                                outZStream.finish();
                                return Convert.ToBase64String(outMemoryStream.ToArray());
                            }
                        } else {
                            return authMessageDecoded.Message.ToString();
                        }
                    } else {
                        return "Error";
                    }
                }

            } catch(Exception) {
                return "Error";
            }
        }

        private static async Task<Ei.EggIncFirstContactResponse> FirstContactNew(string UserId) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        ClientVersion = ClientVersion,
                        Platform = Aux.Platform.Droid,
                        EiUserId = UserId,
                        DeviceId = UserId,
                        Username = "",
                        //GameServicesId = "102371659776481580429", 
                        Rinfo = GetInfo(UserId)
                    }.WriteTo(ms1);
                    //Serializer.Serialize<FirstContactRequestProto>(ms1, new FirstContactRequestProto { UserId = UserId, P2 = 0, P3 = 2 });
                    ms1.Position = 0;
                    var messageData = ms1.ToArray();
                    var ms2 = new MemoryStream();
                    new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) }.WriteTo(ms2);

                    ms2.Position = 0;
                    var sr = new StreamReader(ms2);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                    HttpResponseMessage response;

                    //try {
                    response = await client.PostAsync("ei/first_contact_secure", bac);
                    //} catch(Exception) {
                    //    await Task.Delay(500);
                    //    response = await client.PostAsync("ei/first_contact", bac);
                    //}

                    string r;
                    if(response.IsSuccessStatusCode) {
                        r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());


                        var backup = GetFromAuthenticatedMessage<Ei.EggIncFirstContactResponse>(responseString);


                        backup.Success = true;
                        return backup;
                    } else {
                        return new Ei.EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                    }
                }

            } catch(Exception e) {
                return new Ei.EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }

        public static async Task<Ei.Backup> BotFirstContact(string UserId) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        EiUserId = UserId,
                        DeviceId = "EGG9000"
                    }.WriteTo(ms1);
                    ms1.Position = 0;
                    var sr = new StreamReader(ms1);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                    HttpResponseMessage response;

                    //try {
                    response = await client.PostAsync("ei/bot_first_contact", bac);
                    //} catch(Exception) {
                    //    await Task.Delay(500);
                    //    response = await client.PostAsync("ei/first_contact", bac);
                    //}

                    string r;
                    if(response.IsSuccessStatusCode) {
                        r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());


                        var backup = GetFromAuthenticatedMessage<Ei.Backup>(responseString);


                        //backup.Success = true;
                        return backup;
                    } else {
                        //return new Ei.EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                        return null;
                    }
                }

            } catch(Exception) {
                //return new Ei.EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
                return null;
            }
        }

        public static async Task<T> DiscordRestGetBot<T>(string path, string DiscordToken) {
            using(var client = new HttpClient()) {
                client.BaseAddress = new Uri("https://discordapp.com/api/");
                client.DefaultRequestHeaders.Add("Authorization", "Bot " + DiscordToken);
                var response = await client.GetAsync(path);
                await Task.Delay(500);
                return await response.Content.ReadFromJsonAsync<T>();
            }
        }

        public static async Task<T> DiscordRestPutBot<T, U>(string path, string DiscordToken, U Params) {
            using(var client = new HttpClient()) {
                client.BaseAddress = new Uri("https://discordapp.com/api/");
                client.DefaultRequestHeaders.Add("Authorization", "Bot " + DiscordToken);
                var response = await client.PutAsJsonAsync(path, Params);
                await Task.Delay(500);
                return await response.Content.ReadFromJsonAsync<T>();
            }
        }

        public static async Task<T> DiscordRestGetUser<T>(string path, string DiscordToken) {
            using(var client = new HttpClient()) {
                client.BaseAddress = new Uri("https://discordapp.com/api/");
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + DiscordToken);
                var response = await client.GetAsync(path);
                await Task.Delay(500);
                return await response.Content.ReadFromJsonAsync<T>();
            }
        }

        public static async Task<T> DiscordRestPutUser<T, U>(string path, string DiscordToken, U Params) {
            using(var client = new HttpClient()) {
                client.BaseAddress = new Uri("https://discordapp.com/api/");
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + DiscordToken);
                var response = await client.PutAsJsonAsync(path, Params);
                await Task.Delay(500);
                return await response.Content.ReadFromJsonAsync<T>();
            }
        }

        public static Ei.Backup GetRecentBackup(string EggIncId, IMemoryCache cache) {
            var key = $"Backup-{EggIncId}-long";
            if(cache.TryGetValue(key, out Ei.EggIncFirstContactResponse response)) {
                return response?.Backup;
            } else {
                return null;
            }
        }
    }
}
