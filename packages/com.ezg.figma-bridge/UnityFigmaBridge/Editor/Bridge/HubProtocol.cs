using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityFigmaBridge.Editor.Bridge
{
    public enum HubMessageKind { Welcome, Hello, Reply, Progress, Event, Closed, Bye }

    public sealed class HubFile
    {
        public string ConnectionId, FileKey, FileName, ClientId;
        public int Port;
    }

    public sealed class HubMessage
    {
        public HubMessageKind Kind;
        public string ConnectionId;
        public HubFile File;
        public int Protocol;
        public string Id;
        public bool Ok;
        public JToken Result;
        public string ErrorMessage, ErrorCode;
        public int ExtendMs;
        public string Reason;
    }

    public static class HubProtocol
    {
        public const string AgentPath = "/agent"; // peer-wire.ts AGENT_PATH
        public const int PeerProtocol = 1; // peer-wire.ts PEER_PROTOCOL
        public const int PluginProtocol = 2; // bridge-wire.ts BRIDGE_PROTOCOL
        public const int DefaultPort = 39410; // bridge-wire.ts BRIDGE_DEFAULT_PORT
        public const int PortCount = 10; // bridge-wire.ts BRIDGE_PORT_COUNT
        public const int SplitAboveChars = 15 * 1024 * 1024; // bridge-chunk.ts SPLIT_ABOVE_CHARS
        public const int AssembledMaxChars = 256 * 1024 * 1024; // bridge-chunk.ts ASSEMBLED_MAX_CHARS
        public const int PartTtlSeconds = 60; // bridge-chunk.ts PART_TTL_MS
        public const int ProgressExtendMaxMs = 60_000; // bridge-wire.ts PROGRESS_EXTEND_MAX_MS
        public const int HelloTimeoutMs = 5000; // bridge-wire.ts HELLO_TIMEOUT_MS

        public static string AgentHello() =>
            new JObject { ["kind"] = "agent-hello", ["protocol"] = PeerProtocol }.ToString(Formatting.None);

        public static string Request(string connectionId, string id, string op, JToken payload) =>
            new JObject
            {
                ["kind"] = "send",
                ["connectionId"] = connectionId,
                ["frame"] = new JObject
                {
                    ["kind"] = "request",
                    ["id"] = id,
                    ["op"] = op,
                    ["payload"] = payload ?? JValue.CreateNull(),
                },
            }.ToString(Formatting.None);

        public static HubMessage Parse(string text)
        {
            JObject root;
            try { root = JObject.Parse(text); }
            catch (Exception) { return null; }
            try { return ParseRoot(root); }
            catch (Exception) { return null; }
        }

        static HubMessage ParseRoot(JObject root)
        {
            switch ((string)root["kind"])
            {
                case "agent-welcome":
                    return new HubMessage { Kind = HubMessageKind.Welcome };
                case "closed":
                    return new HubMessage { Kind = HubMessageKind.Closed, ConnectionId = (string)root["connectionId"] };
                case "bye":
                    return new HubMessage { Kind = HubMessageKind.Bye, Reason = (string)root["reason"] };
                case "plugin":
                    return ParsePlugin(root);
                default:
                    return null;
            }
        }

        static HubMessage ParsePlugin(JObject root)
        {
            var connectionId = (string)root["connectionId"];
            if (connectionId == null || !(root["frame"] is JObject frame)) return null;
            switch ((string)frame["kind"])
            {
                case "hello": return ParseHello(root, frame, connectionId);
                case "reply": return ParseReply(frame, connectionId);
                case "progress":
                    return new HubMessage
                    {
                        Kind = HubMessageKind.Progress,
                        ConnectionId = connectionId,
                        Id = (string)frame["id"],
                        ExtendMs = (int)(double)frame["extendMs"],
                    };
                case "event":
                    return new HubMessage { Kind = HubMessageKind.Event, ConnectionId = connectionId };
                default:
                    return null;
            }
        }

        static HubMessage ParseHello(JObject root, JObject frame, string connectionId)
        {
            if (!(frame["file"] is JObject file)) return null;
            return new HubMessage
            {
                Kind = HubMessageKind.Hello,
                ConnectionId = connectionId,
                Protocol = (int)frame["protocol"],
                File = new HubFile
                {
                    ConnectionId = connectionId,
                    FileKey = (string)file["fileKey"],
                    FileName = (string)file["fileName"],
                    ClientId = (string)file["clientId"],
                    Port = (int)root["port"],
                },
            };
        }

        static HubMessage ParseReply(JObject frame, string connectionId)
        {
            var message = new HubMessage
            {
                Kind = HubMessageKind.Reply,
                ConnectionId = connectionId,
                Id = (string)frame["id"],
            };
            var ok = frame["ok"];
            if (ok == null || ok.Type != JTokenType.Boolean) return null;
            message.Ok = (bool)ok;
            if (message.Ok)
            {
                message.Result = frame["result"] ?? JValue.CreateNull();
                return message;
            }
            var error = frame["error"] as JObject;
            message.ErrorMessage = (string)error?["message"] ?? "unknown error";
            message.ErrorCode = (string)(error?["data"] as JObject)?["code"];
            return message;
        }
    }
}
