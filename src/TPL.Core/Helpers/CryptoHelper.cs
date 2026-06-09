using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace TPL.Core.Helpers
{
	/// <summary>
	/// Tiện ích mã hoá/giải mã — trích xuất từ LicenseManager.cs.
	/// Dùng chung cho license encryption và key generation.
	/// </summary>
	public static class CryptoHelper
	{
		/// <summary>Tính MD5 hash của chuỗi, trả về hex string.</summary>
		public static string ComputeMD5(string input)
		{
			using MD5 md5 = MD5.Create();
			byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		/// <summary>Tính SHA256 hash, trả về byte array.</summary>
		public static byte[] GetHashSha256(string text)
		{
			using SHA256 sha256 = SHA256.Create();
			return sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
		}

		/// <summary>Tính SHA256 hash, trả về hex string (uppercase).</summary>
		public static string ComputeSHA256String(string text)
		{
			byte[] bytes = GetHashSha256(text);
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("X2"));
			return sb.ToString();
		}

		/// <summary>Mã hoá AES-256 (CBC, zero IV cho simplicity).</summary>
		public static string Encrypt(string plainText, string secretKey)
		{
			byte[] iv = new byte[16];
			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(secretKey);
			aes.IV = iv;
			ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

			using MemoryStream ms = new();
			using (CryptoStream cs = new(ms, encryptor, CryptoStreamMode.Write))
			{
				using StreamWriter sw = new(cs);
				sw.Write(plainText);
			}
			return Convert.ToBase64String(ms.ToArray());
		}

		/// <summary>Giải mã AES-256 (CBC, zero IV).</summary>
		public static string Decrypt(string cipherText, string secretKey)
		{
			byte[] iv = new byte[16];
			byte[] buffer = Convert.FromBase64String(cipherText);

			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(secretKey);
			aes.IV = iv;
			ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

			using MemoryStream ms = new(buffer);
			using CryptoStream cs = new(ms, decryptor, CryptoStreamMode.Read);
			using StreamReader sr = new(cs);
			return sr.ReadToEnd();
		}
	}
}
