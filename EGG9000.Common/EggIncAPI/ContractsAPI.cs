
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

//using static EGG9000.Bot.Automated.LeaderboardUpdater;

namespace EGG9000.Bot.EggIncAPI {


    public class ContractsAPI {
        public const string BaseAddressNew = "https://www.auxbrain.com/";
        //static string BaseAddressOld = "http://afx-2-dot-auxbrainhome.appspot.com/";

        public const string UserId = "EI5223299518300160";
        public const uint ClientVersion = 43;

        public static Ei.BasicRequestInfo GetInfo(string UserId, bool noUserID = false) {
            var info = new Ei.BasicRequestInfo {
                ClientVersion = ClientVersion,
                Version = "1.25",
                Build = "111213",
                Platform = "ANDROID"
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

        public static async Task<TResponse> Post<TResponse, TRequest>(TRequest data, string UserId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : Google.Protobuf.IMessage {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);
                    var ms1 = new MemoryStream();
                    var outCodedStream = new CodedOutputStream(ms1);

                    var url = "";
                    switch(data) {
                        case Ei.JoinCoopRequest e: {
                            url = "ei/join_coop";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        }
                        case Ei.GetPeriodicalsRequest e:
                            url = "ei/get_periodicals";
                            e.Rinfo = GetInfo(UserId, true);
                            e.WriteTo(ms1);
                            break;
                        case Ei.CreateCoopRequest e:
                            url = "ei/create_coop";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        case Ei.QueryCoopRequest e:
                            url = "ei/query_coop";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        case Ei.UpdateCoopPermissionsRequest e:
                            url = "ei/update_coop_permissions";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        case Ei.ContractCoopStatusUpdateRequest e:
                            url = "ei/contract_coop_status_update";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        case Ei.ConfigRequest e:
                            url = "ei/get_config";
                            e.Rinfo = GetInfo(UserId);
                            e.WriteTo(ms1);
                            break;
                        //case Ei.EggIncFirstContactResponse e:
                        //    url = "ei/first_contact";
                        //    e.Rinfo = GetInfo(UserId);
                        //    e.WriteTo(ms1);
                        //    break;
                        default:
                            throw new Exception($"Missing Info for {typeof(TRequest).Name}");
                    }

                    ms1.Position = 0;
                    var sr = new StreamReader(ms1);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
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
            } catch(Exception) {
                return default(TResponse);
            }
        }

        public static async Task<Ei.PeriodicalsResponse> GetPeriodicalsAsync() {
            return await ContractsAPI.Post<Ei.PeriodicalsResponse, Ei.GetPeriodicalsRequest>(new Ei.GetPeriodicalsRequest {
                UserId = ContractsAPI.UserId,
                PiggyFull = false,
                PiggyFoundFull = false,
                SecondsFullRealtime = 2339576.17448521,
                SecondsFullGametime = 391564.659540082,
                SoulEggs = 570149167.28294,
                CurrentClientVersion = ClientVersion,
                Debug = false,
            }, ContractsAPI.UserId, true);
        }

        //public static async Task<Ei.PeriodicalsResponse> GetContracts() {
        //    try {
        //        var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
        //        using(var client = new HttpClient(handler)) {
        //            client.BaseAddress = new Uri("http://www.auxbrain.com/");
        //            var ms1 = new MemoryStream();
        //            Serializer.Serialize<GetPeriodicalsRequest>(ms1, new GetPeriodicalsRequest {
        //                /*user_id = "3216497321658",
        //                piggy_full = 1,
        //                piggy_found_full = 0,
        //                seconds_full_gametime = 1,
        //                seconds_full_realtime = 1,
        //                soul_eggs = 3216546461,*/
        //                soul_eggs = 4916605850998073131,
        //                current_client_version = 27,
        //                debug = 0
        //            });
        //            ms1.Position = 0;
        //            var sr = new StreamReader(ms1);
        //            var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
        //            var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
        //            client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
        //            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
        //            client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
        //            bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

        //            var response = await client.PostAsync("ei/get_periodicals", bac);

        //            if(response.IsSuccessStatusCode) {
        //                var r = await response.Content.ReadAsStringAsync();
        //                var responseString = System.Convert.FromBase64String(r);

        //                var ms = new MemoryStream();
        //                ms.Write(responseString);
        //                ms.Position = 0;

        //                //var c = Serializer.Deserialize<PeriodicalsResponse>(ms);
        //                //c.contracts.Success = true;


        //                //using(StreamWriter file = new StreamWriter("rawproto.txt")) {
        //                //    file.Write(r);
        //                //    file.Close();
        //                //}

        //                var c = Ei.PeriodicalsResponse.Parser.ParseFrom(ms);

        //                return c;
        //            } else {
        //                return null;// new ContractsResponse { Success = false, Error = "Error response from API" };
        //            }
        //        }
        //    } catch(Exception e) {
        //        return null; // new ContractsResponse { Success = false, Error = "Bot Exception: " + e.Message };
        //    }
        //}

        public static async Task<Ei.ContractCoopStatusResponse> GetCoopStatus(string ContractName, string CoopName, CancellationToken cancellationToken = default) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri(BaseAddressNew);

                    var ms1 = new MemoryStream();
                    new Ei.ContractCoopStatusRequest { 
                        ContractIdentifier = ContractName, 
                        CoopIdentifier = CoopName.ToLower(), 
                        Rinfo = GetInfo(ContractsAPI.UserId), 
                        UserId = ContractsAPI.UserId, 
                        ClientVersion = ContractsAPI.ClientVersion }.WriteTo(ms1);
                    //Serializer.Serialize<Ei.ContractCoopStatusRequest>(ms1, new Ei.ContractCoopStatusRequest { ContractIdentifier = ContractName, CoopIdentifier = CoopName.ToLower() });
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


        public static async Task<Ei.EggIncFirstContactResponse> FirstContact(string UserId) {
            if(UserId.StartsWith("EI"))
                return await FirstContactNew(UserId);
            //return new Ei.EggIncFirstContactResponse { Success = false, Error = "Error old ID" };
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri("https://www.auxbrain.com/");

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        ClientVersion = 27,
                        Platform = Aux.Platform.Droid,
                        UserId = UserId
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

        private static async Task<Ei.EggIncFirstContactResponse> FirstContactNew(string UserId) {
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri("https://www.auxbrain.com/");

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest {
                        ClientVersion = 27,
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
                    client.BaseAddress = new Uri("https://www.auxbrain.com/");

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

        public class FirstContactWithRaw {
            public Ei.EggIncFirstContactResponse Response { get; set; }
            public byte[] Raw { get; set; }
        }

        /*public static async Task<FirstContactWithRaw> FirstContactRaw(string UserId) {
            var responseWithRaw = new FirstContactWithRaw();
            try {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using(var client = new HttpClient(handler)) {
                    client.BaseAddress = new Uri("http://www.auxbrain.com/");

                    var ms1 = new MemoryStream();
                    new Ei.EggIncFirstContactRequest { ClientVersion = 27, Platform = Aux.Platform.Droid, UserId = UserId }.WriteTo(ms1);
                    //Serializer.Serialize<FirstContactRequestProto>(ms1, new FirstContactRequestProto { UserId = UserId, P2 = 0, P3 = 2 });
                    ms1.Position = 0;
                    var sr = new StreamReader(ms1);
                    var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                    var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                    client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                    HttpResponseMessage response;

                    try {
                        response = await client.PostAsync("ei/first_contact", bac);
                    } catch(Exception) {
                        await Task.Delay(500);
                        response = await client.PostAsync("ei/first_contact", bac);
                    }

                    string r;
                    if(response.IsSuccessStatusCode) {
                        r = await response.Content.ReadAsStringAsync();
                        var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());

                        var ms = new MemoryStream();
                        ms.Write(responseString);
                        ms.Position = 0;

                        responseWithRaw.Raw = ms.ToArray();
                        ms.Position = 0;

                        var backup = Ei.EggIncFirstContactResponse.Parser.ParseFrom(ms);

                        //var coop = Serializer.Deserialize<Ei.EggIncFirstContactResponse>(ms);
                        backup.Success = true;
                        responseWithRaw.Response = backup;
                        return responseWithRaw;
                    } else {
                        responseWithRaw.Response = new Ei.EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                    }
                }

            } catch(Exception e) {
                responseWithRaw.Response = new Ei.EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
            return responseWithRaw;
        }
        */

        public static Ei.Backup GetRecentBackup(string EggIncId, IMemoryCache cache) {
            var key = $"Backup-{EggIncId}-long";
            if(cache.TryGetValue(key, out Ei.EggIncFirstContactResponse response)) {
                return response?.Backup;
            } else {
                return null;
            }
        }




        //public static async Task<List<LeaderboardUser>> GetUserBackups(IMemoryCache cache, List<DBUser> users, bool longBackup = false) {
        //    var lUsers = new List<LeaderboardUser>();



        //    //if (HideDisabled) users = users.Where(x => !x.TempDisabled).ToList();

        //    //if(GuildId > 0) {
        //    //    users = users.Where(x => x.GuildId == GuildId).ToList();
        //    //}
        //    var tasks = users.Select(async (user) => {
        //        List<Ei.Backup> backups;
        //        var key = $"Backup-{user.Id}";
        //        if(!cache.TryGetValue(key + (longBackup ? "-long" : ""), out backups)) {
        //            backups = new List<Ei.Backup>();
        //            foreach(var egginc in user.EggIncIds.Where(x => !string.IsNullOrWhiteSpace(x.Id))) {
        //                var response = await ContractsAPI.FirstContact(egginc.Id);
        //                if(response.Success && response.Backup != null && response.Backup.Settings != null) {
        //                    backups.Add(response.Backup);
        //                } else if(user.LastBackup != null && user.LastBackup.Any(x => x.UserId == egginc.Id)) {
        //                    backups.Add(user.LastBackup.First(x => x.GetID() == egginc.Id));
        //                }
        //            }
        //            var saveBackups = user.LastBackup == null || backups.Count != user.LastBackup.Count;
        //            if(!saveBackups) {
        //                foreach(var backup in backups) {
        //                    var cBackup = user.LastBackup?.FirstOrDefault(x => x.UserId == backup.GetID());
        //                    if(cBackup?.Settings?.LastBackupTime != backup.Settings?.LastBackupTime) {
        //                        saveBackups = true;
        //                        break;
        //                    }
        //                }
        //            }
        //            if(saveBackups && backups.Count > 0) {
        //                user.LastBackup = backups;
        //            }
        //            cache.Set(key, backups, DateTimeOffset.Now.AddMinutes(5));
        //            cache.Set(key + "-long", backups, DateTimeOffset.Now.AddDays(30));
        //        }



        //        foreach(var egginc in user.EggIncIds.Where(x => !string.IsNullOrWhiteSpace(x.Id))) {
        //            var lUser = new LeaderboardUser {
        //                Backup = backups.FirstOrDefault(x => x?.GetID() == egginc.Id),
        //                User = user
        //            };
        //            if(lUser.Backup?.Game == null) {
        //                Console.WriteLine($"Missing backup for {user.DiscordUsername}");
        //                lUser.Backup = user.LastBackup?.FirstOrDefault(x => x?.UserId == egginc.Id);
        //            }
        //            if(lUser.Backup != null) {
        //                lUsers.Add(lUser);
        //            }
        //        }
        //    });

        //    await Task.WhenAll(tasks);

        //    //foreach(var lUser in lUsers) {
        //    //    var lastSeen = await _db.UserCoopStatuses.Where(x => x.UserId == lUser.User.Id).OrderByDescending(x => x.CreatedOn).FirstOrDefaultAsync();
        //    //    lUser.lastSeen = lastSeen?.CreatedOn;
        //    //}


        //    return lUsers;
        //}
    }
}
