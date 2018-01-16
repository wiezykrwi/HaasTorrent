using System;

namespace HaasTorrent.Cli
{
	public static class Log
	{
		private static readonly object Lock = new object();

		public static void Debug(string message)
		{
			lock (Lock)
			{
				Console.ForegroundColor = ConsoleColor.Blue;
				Console.WriteLine($"DBG: {message}");
			}
		}

		public static void Info(string message)
		{
			lock (Lock)
			{
				Console.ForegroundColor = ConsoleColor.White;
				Console.WriteLine($"INF: {message}");
			}
		}

		public static void Warning(string message)
		{
			lock (Lock)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"WRN: {message}");
			}
		}

		public static void Error(string message)
		{
			lock (Lock)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"ERR: {message}");
			}
		}
	}
}