using System;
using System.Text;

namespace TPL.Core.Helpers
{
	/// <summary>
	/// Base32 encode/decode — trích xuất từ LicenseManager.cs.
	/// Sử dụng RFC 4648 alphabet (A-Z, 2-7).
	/// </summary>
	public static class Base32
	{
		private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

		/// <summary>Encode 10 bytes → 16 ký tự Base32.</summary>
		public static string Encode(byte[] data)
		{
			if (data == null || data.Length != 10)
				throw new ArgumentException("Data must be exactly 10 bytes.");

			char[] chars = new char[16];
			int charIndex = 0;
			int byteIndex = 0;
			int bitBuffer = 0;
			int bitCount = 0;

			while (charIndex < 16)
			{
				if (bitCount < 5)
				{
					bitBuffer = (bitBuffer << 8) | data[byteIndex++];
					bitCount += 8;
				}
				int index = (bitBuffer >> (bitCount - 5)) & 0x1F;
				bitCount -= 5;
				chars[charIndex++] = Alphabet[index];
			}
			return new string(chars);
		}

		/// <summary>Decode 16 ký tự Base32 → 10 bytes.</summary>
		public static byte[] Decode(string input)
		{
			StringBuilder sb = new();
			foreach (char c in input.ToUpper())
			{
				if (Alphabet.IndexOf(c) >= 0)
					sb.Append(c);
			}
			string sanitized = sb.ToString();
			if (sanitized.Length != 16)
				throw new ArgumentException("Invalid Base32 string length.");

			byte[] data = new byte[10];
			int byteIndex = 0;
			int bitBuffer = 0;
			int bitCount = 0;

			for (int i = 0; i < 16; i++)
			{
				int value = Alphabet.IndexOf(sanitized[i]);
				bitBuffer = (bitBuffer << 5) | value;
				bitCount += 5;

				if (bitCount >= 8)
				{
					data[byteIndex++] = (byte)((bitBuffer >> (bitCount - 8)) & 0xFF);
					bitCount -= 8;
				}
			}
			return data;
		}
	}
}
