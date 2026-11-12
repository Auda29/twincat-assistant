using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TwinCatAssistant.Native;

internal static class EngineeringHostTransportProbe
{
    static int calls;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
    static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    static byte[] Unhex(string s) { var b = new byte[s.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16); return b; }
    static string Hash(string s) { using (var h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s))); }
    static string Sign(string key, string value) { using (var h = new HMACSHA256(Unhex(key))) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(value))); }
    static string Transcript(string domain, string session, string server, string client, string hash) { return "TCMCP1\n" + domain + "\n" + session + "\n" + server + "\n" + client + (hash == null ? "" : "\n" + hash); }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Write(StreamWriter w, string value) { w.WriteLine(value); w.Flush(); }
    static string Exchange(EngineeringHostTransport host, string mode, string request)
    {
        using (var pipe = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut))
        {
            pipe.Connect(5000);
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 1024, true))
            
            {
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { NewLine = "\n" };
                string helloText = reader.ReadLine();
                Check(helloText != null && !helloText.Contains(host.Capability), "Secret leaked in hello.");
                var hello = Json.Deserialize<Dictionary<string, object>>(helloText);
                string session = (string)hello["sessionId"], server = (string)hello["serverNonce"], client = new string('a', 64);
                Check(session == host.SessionId, "Wrong session.");
                string proof = Sign(host.Capability, Transcript("client", session, server, client, null));
                if (mode == "bad-auth") proof = new string('0', 64);
                if (mode == "duplicate") { Write(writer, "{\"clientNonce\":\"" + client + "\",\"clientNonce\":\"" + client + "\",\"proof\":\"" + proof + "\"}"); return reader.ReadLine(); }
                Write(writer, Json.Serialize(new { clientNonce = client, proof = proof }));
                string ackText = reader.ReadLine();
                if (mode == "bad-auth") return ackText;
                Check(ackText != null && !ackText.Contains(host.Capability), "Secret leaked in ack.");
                var ack = Json.Deserialize<Dictionary<string, string>>(ackText);
                Check(ack["proof"] == Sign(host.Capability, Transcript("server", session, server, client, null)), "Invalid server proof.");
                if (mode == "malformed") { Write(writer, "{\"protocolVersion\":1,\"request\":\"\\q\",\"proof\":\"x\"}"); return reader.ReadLine(); }
                proof = Sign(host.Capability, Transcript("request", session, server, client, Hash(request)));
                if (mode == "bad-request") proof = new string('0', 64);
                Write(writer, Json.Serialize(new { protocolVersion = 1, request = request, proof = proof }));
                string result = reader.ReadLine();
                if (mode == "bad-request") return result;
                Check(result != null && !result.Contains(host.Capability), "Secret leaked in response.");
                var response = Json.Deserialize<Dictionary<string, string>>(result);
                Check(response["proof"] == Sign(host.Capability, Transcript("response", session, server, client, Hash(response["response"]))), "Invalid response proof.");
                return response["response"];
            }
        }
    }
    public static int Main()
    {
        try
        {
            var host = new EngineeringHostTransport(Guid.NewGuid(), (value, ct) => { Interlocked.Increment(ref calls); return Task.FromResult(value); });
            string request = "{\"sessionId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"operationId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}";
            Check(Exchange(host, "valid", request) == request, "Valid request changed.");
            Check(Exchange(host, "bad-auth", request) == null, "Wrong authentication accepted.");
            Check(Exchange(host, "duplicate", request) == null, "Duplicate key accepted.");
            Check(Exchange(host, "malformed", request) == null, "Malformed request accepted.");
            Check(Exchange(host, "bad-request", request) == null, "Wrong request proof accepted.");
            using (var large = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut))
            {
                large.Connect(5000);
                using (var reader = new StreamReader(large, Encoding.UTF8, false, 1024, true))
                {
                    Check(reader.ReadLine() != null, "Missing greeting.");
                    byte[] oversized = Encoding.UTF8.GetBytes(new string('x', 4097) + "\n");
                    large.Write(oversized, 0, oversized.Length); large.Flush();
                    Check(reader.ReadLine() == null, "Oversized handshake accepted.");
                }
            }
            Check(calls == 1, "Rejected request reached handler.");
            Check(Exchange(host, "valid", request) == request, "Server wedged after invalid frame.");
            using (var waiting = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut))
            {
                waiting.Connect(5000);
                using (var reader = new StreamReader(waiting, Encoding.UTF8, false, 1024, true))
                {
                    Check(reader.ReadLine() != null, "Missing greeting.");
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Check(reader.ReadLine() == null, "Idle connection was not closed.");
                    Check(clock.Elapsed.TotalSeconds >= 25 && clock.Elapsed.TotalSeconds < 40, "Unexpected idle timeout.");
                }
            }
            using (var idle = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut))
            {
                idle.Connect(5000);
                using (var reader = new StreamReader(idle, Encoding.UTF8, false, 1024, true)) { Check(reader.ReadLine() != null, "Missing greeting."); host.Dispose(); Check(reader.ReadLine() == null, "Dispose did not close connection."); }
            }
            host.Dispose();
            Console.WriteLine("Engineering transport probe passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}


