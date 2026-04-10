using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Windwalk.Net;

namespace Andromeda.Mod.Networking.Sync
{
    public sealed class NetworkSettingsSyncMessage : NetMessage
    {
        public const short MessageType = 250;

        public string source;
        public int revision;
        public string[] keys;
        public string[] values;

        public override NetMessage.Type MsgType => (NetMessage.Type)MessageType;

        public static NetworkSettingsSyncMessage FromSnapshot(IReadOnlyDictionary<string, string> snapshot, string source, int revision)
        {
            var msg = new NetworkSettingsSyncMessage
            {
                source = source ?? "unknown",
                revision = revision,
                keys = new string[snapshot?.Count ?? 0],
                values = new string[snapshot?.Count ?? 0]
            };

            if (snapshot == null)
                return msg;

            int i = 0;
            foreach (var kv in snapshot)
            {
                msg.keys[i] = kv.Key;
                msg.values[i] = kv.Value ?? string.Empty;
                i++;
            }

            return msg;
        }

        public Dictionary<string, string> ToSnapshot()
        {
            var snapshot = new Dictionary<string, string>();
            int count = Math.Min(keys?.Length ?? 0, values?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(keys[i]))
                    continue;

                snapshot[keys[i]] = values[i] ?? string.Empty;
            }

            return snapshot;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(source ?? string.Empty);
            writer.WritePackedUInt32((uint)Mathf.Max(0, revision));

            int count = Math.Min(keys?.Length ?? 0, values?.Length ?? 0);
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(keys[i] ?? string.Empty);
                writer.Write(values[i] ?? string.Empty);
            }
        }

        public override void Deserialize(NetworkReader reader)
        {
            source = reader.ReadString();
            revision = (int)reader.ReadPackedUInt32();

            int count = reader.ReadUInt16();
            keys = new string[count];
            values = new string[count];
            for (int i = 0; i < count; i++)
            {
                keys[i] = reader.ReadString();
                values[i] = reader.ReadString();
            }
        }
    }
}
