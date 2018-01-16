using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HaasTorrent.Cli
{
	class Program
	{
		static void Main(string[] args)
		{
			// torrent doc: http://www.seanjoflynn.com

			// magnet url registration: https://msdn.microsoft.com/en-us/library/aa767914(v=vs.85).aspx
			// cfr gogdownloader

			var magnet =
				"magnet:?xt=urn:btih:e9d40b54afc2957d7ba64fbdf420d33dcb916db8&dn=They+Are+Billions+v0.4.9.51&tr=udp%3A%2F%2Ftracker.leechers-paradise.org%3A6969&tr=udp%3A%2F%2Fzer0day.ch%3A1337&tr=udp%3A%2F%2Fopen.demonii.com%3A1337&tr=udp%3A%2F%2Ftracker.coppersurfer.tk%3A6969&tr=udp%3A%2F%2Fexodus.desync.com%3A6969";

			var random = new Random();

			var hashBytes = CreateBytes("e9d40b54afc2957d7ba64fbdf420d33dcb916db8".ToUpper());
			var clientId = $"-HT0001-{string.Join("", Enumerable.Range(1, 12).Select(_ => random.Next(10)))}";
			var clientBytes = Encoding.UTF8.GetBytes(clientId);

			var sentTransactionId = (uint)random.Next();

			var connectPacket = GetConnectPacket(sentTransactionId);

			var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			sock.Connect("tracker.leechers-paradise.org", 6969);
			
			Console.WriteLine($"INF: Sending connect packet");
			sock.Send(connectPacket.ToArray());
			Console.WriteLine($"Tx > {connectPacket.Count} bytes");

			var buffer = new byte[4 * 1024];
			var read = sock.Receive(buffer);
			Console.WriteLine($"Rx < {read} bytes");

			if (read != 16)
			{
				Console.WriteLine($"ERR: Expecting 16 bytes");
				Console.ReadKey(true);
				return;
			}

			var action = BitConverter.ToUInt32(buffer.Take(4).Reverse().ToArray(), 0);

			if (action != 0U)
			{
				Console.WriteLine($"ERR: Received wrong action");
				Console.ReadKey(true);
				return;
			}

			var receivedTransactionId = BitConverter.ToUInt32(buffer.Skip(4).Take(4).Reverse().ToArray(), 0);

			if (sentTransactionId != receivedTransactionId)
			{
				Console.WriteLine($"ERR: Received wrong transaction id");
				Console.ReadKey(true);
				return;
			}

			var connectionId = BitConverter.ToUInt64(buffer.Skip(4 + 4).Take(8).Reverse().ToArray(), 0);
			var connectionIdData = buffer.Skip(4 + 4).Take(8).ToArray();

			Console.WriteLine($"INF: Negotiated connection id {connectionId}");

			sentTransactionId = (uint)random.Next();

			var announcePacket = GetAnnouncePacket(connectionIdData, sentTransactionId, hashBytes, clientBytes);

			Console.WriteLine($"INF: Sending announce packet");
			sock.Send(announcePacket.ToArray());
			Console.WriteLine($"Tx > {announcePacket.Count} bytes");

			buffer = new byte[4 * 1024];
			read = sock.Receive(buffer);
			Console.WriteLine($"Rx < {read} bytes");

			action = BitConverter.ToUInt32(buffer.Take(4).Reverse().ToArray(), 0);

			if (action != 1U)
			{
				Console.WriteLine($"ERR: Received wrong action");
				if (action == 3U)
				{
					receivedTransactionId = BitConverter.ToUInt32(buffer.Skip(4).Take(4).Reverse().ToArray(), 0);
					if (sentTransactionId != receivedTransactionId)
					{
						Console.WriteLine($"ERR: Received wrong transaction id");
						Console.ReadKey(true);
						return;
					}

					var errorMessage = Encoding.ASCII.GetString(buffer.Skip(4+4).ToArray());
					Console.WriteLine($"ERR: {errorMessage}");
				}
				Console.ReadKey(true);
				return;
			}

			action = BitConverter.ToUInt32(buffer.Take(4).Reverse().ToArray(), 0);
			receivedTransactionId = BitConverter.ToUInt32(buffer.Skip(4).Take(4).Reverse().ToArray(), 0);
			var interval = BitConverter.ToUInt32(buffer.Skip(8).Take(4).Reverse().ToArray(), 0);
			var leechers = BitConverter.ToUInt32(buffer.Skip(12).Take(4).Reverse().ToArray(), 0);
			var seeders = BitConverter.ToUInt32(buffer.Skip(16).Take(4).Reverse().ToArray(), 0);

			var handshakePacket = GetHandshakePacket(hashBytes, clientBytes);

			var result = string.Join("", handshakePacket.Select(c => Convert.ToString(c, 16).ToUpper().PadLeft(2, '0')));

			var offset = 20;
			var peers = new List<Tuple<IPAddress, ushort>>();

			while (read > offset)
			{
				var ipa = buffer[offset];
				var ipb = buffer[offset + 1];
				var ipc = buffer[offset + 2];
				var ipd = buffer[offset + 3];

				var port = BitConverter.ToUInt16(buffer.Skip(offset + 4).Take(2).Reverse().ToArray(), 0);

				var ipAddress = new IPAddress(new[] { ipa, ipb, ipc, ipd });
				peers.Add(new Tuple<IPAddress, ushort>(ipAddress, port));

				offset += 6;
			}

			var tasks = peers.Select(peer =>
			{
				return Task.Run(async () =>
				{
					Log.Info($"{peer.Item1} - attempting connection");
					var tcpClient = new TcpClient();

					try
					{
						await tcpClient.ConnectAsync(peer.Item1, peer.Item2);
					}
					catch (Exception exception)
					{
						Log.Error($"{peer.Item1} - connection failed ({exception.Message})");
						return;
					}

					var networkStream = tcpClient.GetStream();
					var clientBuffer = new byte[4 * 1024];

					await networkStream.WriteAsync(handshakePacket, 0, handshakePacket.Length);
					var readTcp = await networkStream.ReadAsync(clientBuffer, 0, 4 * 1024);

					if (readTcp == 0)
					{
						Log.Warning($"{peer.Item1} - received shutdown");
						tcpClient.Close();
						return;
					}

					Log.Info($"{peer.Item1} - communicating");
				});
			});

			Task.WaitAll(tasks.ToArray());

			

			Console.ReadKey(true);
		}

		private static byte[] GetHandshakePacket(byte[] hashBytes, byte[] clientBytes)
		{
			var handshakePacket = new List<byte>();

			handshakePacket.Add(19);
			handshakePacket.AddRange(Encoding.UTF8.GetBytes("BitTorrent protocol"));
			handshakePacket.AddRange(Enumerable.Repeat((byte) 0, 8));
			handshakePacket.AddRange(hashBytes);
			handshakePacket.AddRange(clientBytes);

			return handshakePacket.ToArray();
		}

		private static List<byte> GetAnnouncePacket(byte[] connectionIdData, uint sentTransactionId, byte[] hashBytes, byte[] clientBytes)
		{
			var announcePacket = new List<byte>();

			announcePacket.AddRange(connectionIdData);
			announcePacket.AddRange(BitConverter.GetBytes(1).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(sentTransactionId).Reverse());
			announcePacket.AddRange(hashBytes);
			announcePacket.AddRange(clientBytes);
			announcePacket.AddRange(BitConverter.GetBytes((long) 0).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes((long) 0).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes((long) 0).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(2).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(0).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(0).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(-1).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes((ushort) 44444).Reverse());

			return announcePacket;
		}

		private static List<byte> GetConnectPacket(uint sentTransactionId)
		{
			var connectPacket = new List<byte>();

			connectPacket.AddRange(BitConverter.GetBytes(0x41727101980U).Reverse());
			connectPacket.AddRange(BitConverter.GetBytes((uint) 0).Reverse());
			connectPacket.AddRange(BitConverter.GetBytes(sentTransactionId).Reverse());

			return connectPacket;
		}

		private static byte[] CreateBytes(string input)
		{
			var regex = new Regex(@"[0-9A-F]{2}");
			var matches = regex.Matches(input);
			return (from Match match in matches
				where match.Value != "0x"
				select Convert.ToByte(match.Value, 16)
			).ToArray();
		}
	}
}
