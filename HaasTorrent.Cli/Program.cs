using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

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

			var announcePacket = GetAnnouncePacket(connectionIdData, sentTransactionId);

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
			Console.ReadKey(true);
		}

		private static List<byte> GetAnnouncePacket(byte[] connectionIdData, uint sentTransactionId)
		{
			var announcePacket = new List<byte>();
			announcePacket.AddRange(connectionIdData);
			announcePacket.AddRange(BitConverter.GetBytes(1).Reverse());
			announcePacket.AddRange(BitConverter.GetBytes(sentTransactionId).Reverse());
			announcePacket.AddRange(CreateBytes("e9d40b54afc2957d7ba64fbdf420d33dcb916db8".ToUpper()));
			announcePacket.AddRange(Encoding.ASCII.GetBytes("Haas Torrent v 0.0.1"));
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
