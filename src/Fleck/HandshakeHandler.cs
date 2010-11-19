﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Fleck
{
	internal class HandshakeHandler
	{
		public HandshakeHandler(string origin, string location)
		{
			Origin = origin;
			Location = location;
		}

		public string Origin { get; set; }
		public string Location { get; set; }
		public ClientHandshake ClientHandshake { get; set; }
		public Action<ClientHandshake> OnSuccess { get; set; }


		public void Shake(Socket socket)
		{
			try
			{
				var state = new HandShakeState {Socket = socket};
				state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0,
				                          r =>
				                          	{
				                          		int received = socket.EndReceive(r);
				                          		DoShake(state, received);
				                          	}, null);
			}
			catch (Exception e)
			{
				Log.Error("Exception thrown from method Receive:\n" + e.Message);
			}
		}

		private void DoShake(HandShakeState state, int receivedByteCount)
		{
			ClientHandshake = ParseClientHandshake(new ArraySegment<byte>(state.Buffer, 0, receivedByteCount));


			if (ClientHandshake.Validate(Origin, Location))
			{
				ServerHandshake serverShake = GenerateResponseHandshake();
				BeginSendServerHandshake(serverShake, state.Socket);
			}
			else
			{
				state.Socket.Close();
				return;
			}
		}

		private static ClientHandshake ParseClientHandshake(ArraySegment<byte> byteShake)
		{
			const string pattern = @"^(?<connect>[^\s]+)\s(?<path>[^\s]+)\sHTTP\/1\.1\r\n" + // request line
			                       @"((?<field_name>[^:\r\n]+):\s(?<field_value>[^\r\n]+)\r\n)+";

			var handshake = new ClientHandshake();
			var challenge = new ArraySegment<byte>(byteShake.Array, byteShake.Count - 8, 8); // -8 : eight byte challenge
			handshake.ChallengeBytes = challenge;

			string utf8Handshake = Encoding.UTF8.GetString(byteShake.Array, 0, byteShake.Count - 8);

			var regex = new Regex(pattern, RegexOptions.IgnoreCase);
			Match match = regex.Match(utf8Handshake);
			GroupCollection fields = match.Groups;

			handshake.ResourcePath = fields["path"].Value;

			for (int i = 0; i < fields["field_name"].Captures.Count; i++)
			{
				string name = fields["field_name"].Captures[i].ToString();
				string value = fields["field_value"].Captures[i].ToString();

				switch (name.ToLower())
				{
					case "sec-websocket-key1":
						handshake.Key1 = value;
						break;
					case "sec-websocket-key2":
						handshake.Key2 = value;
						break;
					case "sec-websocket-protocol":
						handshake.SubProtocol = value;
						break;
					case "origin":
						handshake.Origin = value;
						break;
					case "host":
						handshake.Host = value;
						break;
					case "cookie":
						handshake.Cookies = value;
						break;
					default:
						if (handshake.AdditionalFields == null)
							handshake.AdditionalFields = new Dictionary<string, string>();
						handshake.AdditionalFields[name] = value;
						break;
				}
			}
			return handshake;
		}

		private ServerHandshake GenerateResponseHandshake()
		{
			var responseHandshake = new ServerHandshake
			{
				Location = "ws://" + ClientHandshake.Host + ClientHandshake.ResourcePath,
				Origin = ClientHandshake.Origin,
				SubProtocol = ClientHandshake.SubProtocol
			};

			var challenge = new byte[8];
			Array.Copy(ClientHandshake.ChallengeBytes.Array, ClientHandshake.ChallengeBytes.Offset, challenge, 0, 8);

			responseHandshake.AnswerBytes =
				CalculateAnswerBytes(ClientHandshake.Key1, ClientHandshake.Key2, ClientHandshake.ChallengeBytes);

			return responseHandshake;
		}

		private void BeginSendServerHandshake(ServerHandshake handshake, Socket socket)
		{
			string stringShake = handshake.ToResponseString();

			byte[] byteResponse = Encoding.UTF8.GetBytes(stringShake);
			int byteResponseLength = byteResponse.Length;
			Array.Resize(ref byteResponse, byteResponseLength + handshake.AnswerBytes.Length);
			Array.Copy(handshake.AnswerBytes, 0, byteResponse, byteResponseLength, handshake.AnswerBytes.Length);

			socket.BeginSend(byteResponse, 0, byteResponse.Length, 0,
			                 r =>
			                 	{
			                 		socket.EndSend(r);
			                 		EndSendServerHandshake();
			                 	}
			                 , null);
		}

		private void EndSendServerHandshake()
		{
			if (OnSuccess != null)
				OnSuccess(ClientHandshake);
		}

		private static byte[] CalculateAnswerBytes(string key1, string key2, ArraySegment<byte> challenge)
		{
			byte[] result1Bytes = ParseKey(key1);
			byte[] result2Bytes = ParseKey(key2);

			var rawAnswer = new byte[16];
			Array.Copy(result1Bytes, 0, rawAnswer, 0, 4);
			Array.Copy(result2Bytes, 0, rawAnswer, 4, 4);
			Array.Copy(challenge.Array, challenge.Offset, rawAnswer, 8, 8);

			MD5 md5 = MD5.Create();
			return md5.ComputeHash(rawAnswer);
		}

		private static byte[] ParseKey(string key)
		{
			int spaces = key.Count(x => x == ' ');
			var digits = new String(key.Where(Char.IsDigit).ToArray());

			var value = (Int32) (Int64.Parse(digits)/spaces);

			byte[] result = BitConverter.GetBytes(value);
			if (BitConverter.IsLittleEndian)
				Array.Reverse(result);
			return result;
		}

		private class HandShakeState
		{
			private const int BufferSize = 1024;
			public readonly byte[] Buffer = new byte[BufferSize];
			public Socket Socket { get; set; }
		}
	}
}