using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TPL
{
	public class LicenseInfo
	{
		public string HardwareId { get; set; }
		public DateTime TrialStartDate { get; set; }
		public DateTime ExpirationDate { get; set; }
		public DateTime LastRunDate { get; set; }
		public List<string> AppliedKeys { get; set; } = new List<string>();
		public bool IsHardwareChanged { get; set; } = false;

		public bool IsValid
		{
			get
			{
				if (IsHardwareChanged) return false;
				if (DateTime.Now < LastRunDate) return false; // Clock was turned back
				if (DateTime.Now > ExpirationDate && ExpirationDate != DateTime.MaxValue) return false;
				return true;
			}
		}

		public string Serialize()
		{
			return $"{HardwareId};{TrialStartDate.Ticks};{ExpirationDate.Ticks};{LastRunDate.Ticks};{string.Join(",", AppliedKeys)}";
		}

		public static LicenseInfo Deserialize(string data)
		{
			var parts = data.Split(';');
			var info = new LicenseInfo
			{
				HardwareId = parts[0],
				TrialStartDate = new DateTime(long.Parse(parts[1])),
				ExpirationDate = new DateTime(long.Parse(parts[2])),
				LastRunDate = new DateTime(long.Parse(parts[3]))
			};
			if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
			{
				info.AppliedKeys = new List<string>(parts[4].Split(','));
			}
			return info;
		}
	}

	public static class LicenseManager
	{
		private const string SecretKey = "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!"; // 256-bit+
		private const string RegistryPath = @"Software\TPL\Settings";

		// Thay link Google Sheets (định dạng CSV) của bạn vào đây. Xem hướng dẫn đi kèm.
		public const string RevokeListUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vQINX-Qrie3CV-wo3xMZU7gwkMKcbBORTTQryY8af60V3sxG7_Q1QspoQ3o7GmxJmVTH5Q5_vfOWUr8/pub?gid=198057700&single=true&output=csv";

		public static void CheckRemoteRevokeAsync()
		{
			if (string.IsNullOrEmpty(RevokeListUrl) || !RevokeListUrl.StartsWith("http")) return;

			Task.Run(() =>
			{
				try
				{
					// Tải danh sách các mã bị khoá từ xa
					using var client = new WebClient();
					// Chống cache từ Windows và Google để luôn nhận dữ liệu mới nhất
					client.Headers.Add("Cache-Control", "no-cache");
					string url = RevokeListUrl + (RevokeListUrl.Contains("?") ? "&" : "?") + "_t=" + DateTime.Now.Ticks;
					string data = client.DownloadString(url);
					string hwId = GetHardwareId().ToUpper();

					// Chuẩn hóa danh sách blacklist tải về (xóa dấu gạch ngang, khoảng trắng, chuyển in hoa)
					string cleanData = data.Replace("-", "").Replace(" ", "").ToUpper();

					bool isRevoked = false;
					if (cleanData.Contains(hwId))
					{
						isRevoked = true;
					}
					else
					{
						LicenseInfo info = GetLicenseInfo();
						foreach (string key in info.AppliedKeys)
						{
							if (!string.IsNullOrEmpty(key) && cleanData.Contains(key.ToUpper()))
							{
								isRevoked = true;
								break;
							}
						}
					}

					if (isRevoked)
					{
						LicenseInfo info = GetLicenseInfo();
						if (info.IsValid)
						{
							info.ExpirationDate = DateTime.MinValue; // Lập tức hết hạn
							SaveLicenseInfo(info);
						}
					}
				}
				catch { /* Bỏ qua nếu không có mạng */ }
			});
		}

		public static string GetHardwareId()
		{
			string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
			string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
			return ComputeMD5(cpuId + boardId).ToUpper();
		}

		private static string GetWmiProperty(string wmiclass, string property)
		{
			try
			{
				using ManagementObjectSearcher searcher = new($"SELECT {property} FROM {wmiclass}");
				foreach (ManagementBaseObject obj in searcher.Get())
				{
					return obj[property]?.ToString()?.Trim() ?? "";
				}
			}
			catch { }
			return "UNKNOWN";
		}

		private static string ComputeMD5(string input)
		{
			using MD5 md5 = MD5.Create();
			byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		public static LicenseInfo GetLicenseInfo()
		{
			string hwId = GetHardwareId();
			string encryptedData = null;
			try
			{
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
				encryptedData = key?.GetValue("LicenseData") as string;
			}
			catch { }

			if (string.IsNullOrEmpty(encryptedData))
			{
				// First time run
				var info = new LicenseInfo
				{
					HardwareId = hwId,
					TrialStartDate = DateTime.Now,
					ExpirationDate = DateTime.Now.AddDays(30),
					LastRunDate = DateTime.Now,
					AppliedKeys = new List<string>()
				};
				SaveLicenseInfo(info);
				return info;
			}

			try
			{
				string decrypted = Decrypt(encryptedData);
				var info = LicenseInfo.Deserialize(decrypted);

				if (info.HardwareId != hwId)
				{
					info.IsHardwareChanged = true;
				}
				return info;
			}
			catch
			{
				// Tampered or corrupted
				return new LicenseInfo { HardwareId = hwId, ExpirationDate = DateTime.MinValue, LastRunDate = DateTime.Now, IsHardwareChanged = true };
			}
		}

		public static void SaveLicenseInfo(LicenseInfo info)
		{
			try
			{
				string serialized = info.Serialize();
				string encrypted = Encrypt(serialized);
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
				key?.SetValue("LicenseData", encrypted);
			}
			catch { }
		}

		public static void UpdateLastRunDate(LicenseInfo info)
		{
			if (info.IsValid && DateTime.Now >= info.LastRunDate)
			{
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);
			}
		}

		public static bool ActivateLicense(string activationKey, out string message)
		{
			try
			{
				// 1. Chuẩn hóa key: loại bỏ dấu gạch ngang, khoảng trắng, chuyển in hoa
				string cleanedKey = activationKey.Replace("-", "").Replace(" ", "").ToUpper();
				if (cleanedKey.Length != 16)
				{
					message = "Mã kích hoạt phải có độ dài đúng 16 ký tự.";
					return false;
				}

				// 2. Giải mã Base32 thành 10 bytes
				byte[] payload;
				try
				{
					payload = Base32.Decode(cleanedKey);
				}
				catch
				{
					message = "Mã kích hoạt chứa ký tự không hợp lệ.";
					return false;
				}

				if (payload.Length != 10)
				{
					message = "Mã kích hoạt không đúng định dạng nhị phân.";
					return false;
				}

				// 3. Giải nén 10 bytes
				int days = (payload[0] << 8) | payload[1];
				
				byte[] shortHwIdBytes = new byte[4];
				Array.Copy(payload, 2, shortHwIdBytes, 0, 4);
				StringBuilder sbHw = new StringBuilder();
				foreach (byte b in shortHwIdBytes) sbHw.Append(b.ToString("X2"));
				string shortHwIdHex = sbHw.ToString();

				byte seqByte = payload[6];

				byte[] providedSigBytes = new byte[3];
				Array.Copy(payload, 7, providedSigBytes, 0, 3);

				// 4. Xác thực máy tính hiện tại
				string myHwId = GetHardwareId();
				string myShortHwIdHex = myHwId.Substring(0, Math.Min(8, myHwId.Length)).ToUpper();

				if (shortHwIdHex != myShortHwIdHex)
				{
					message = "Mã kích hoạt này không dành cho máy tính (phần cứng) này.";
					return false;
				}

				// 5. Xác thực chữ ký số
				string textToHash = $"{shortHwIdHex}|{days}|{seqByte}|{SecretKey}";
				byte[] hashBytes = GetHashSha256(textToHash);
				
				for (int i = 0; i < 3; i++)
				{
					if (providedSigBytes[i] != hashBytes[i])
					{
						message = "Mã kích hoạt không hợp lệ hoặc đã bị chỉnh sửa.";
						return false;
					}
				}

				LicenseInfo info = GetLicenseInfo();

				// Kiểm tra key đã được dùng chưa (so sánh theo cleanedKey)
				if (info.AppliedKeys.Contains(cleanedKey))
				{
					message = "Mã kích hoạt này đã được sử dụng rồi.";
					return false;
				}

				if (info.IsHardwareChanged)
				{
					info.HardwareId = GetHardwareId();
					info.IsHardwareChanged = false;
				}

				// Nếu đã hết hạn, ngày tính tiếp theo là hôm nay. Nếu chưa hết hạn, cộng dồn.
				DateTime baseDate = DateTime.Now > info.ExpirationDate ? DateTime.Now : info.ExpirationDate;

				if (days >= 9999 || baseDate == DateTime.MaxValue)
				{
					info.ExpirationDate = DateTime.MaxValue; // Vĩnh viễn
				}
				else
				{
					try
					{
						info.ExpirationDate = baseDate.AddDays(days);
					}
					catch (ArgumentOutOfRangeException)
					{
						info.ExpirationDate = DateTime.MaxValue;
					}
				}

				info.AppliedKeys.Add(cleanedKey);
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);

				message = $"Kích hoạt thành công!\n\nHạn sử dụng mới: {(info.ExpirationDate == DateTime.MaxValue ? "Vĩnh viễn" : info.ExpirationDate.ToString("dd/MM/yyyy HH:mm"))}";
				return true;
			}
			catch (Exception ex)
			{
				message = $"Mã kích hoạt không hợp lệ hoặc bị lỗi định dạng: {ex.Message}";
				return false;
			}
		}

		public static string GenerateKey(string hwId, int days, string seq = "")
		{
			// 1. Lấy 8 ký tự đầu của Hardware ID và chuyển sang mảng 4 bytes
			string shortHwIdHex = hwId.Substring(0, Math.Min(8, hwId.Length)).ToUpper().PadRight(8, '0');
			byte[] shortHwIdBytes = new byte[4];
			for (int i = 0; i < 4; i++)
			{
				shortHwIdBytes[i] = Convert.ToByte(shortHwIdHex.Substring(i * 2, 2), 16);
			}

			// 2. Chuyển đổi số ngày sang mảng 2 bytes (big-endian)
			byte[] daysBytes = new byte[2];
			daysBytes[0] = (byte)(days >> 8);
			daysBytes[1] = (byte)(days & 0xFF);

			// 3. Tính toán byte phân biệt (seqByte) từ chuỗi seq
			byte seqByte = 0;
			if (!string.IsNullOrEmpty(seq))
			{
				int sum = 0;
				foreach (char c in seq)
				{
					sum = (sum + (int)c) % 256;
				}
				seqByte = (byte)sum;
			}

			// 4. Tính toán signature từ shortHwIdHex | days | seqByte | SecretKey
			string textToHash = $"{shortHwIdHex}|{days}|{seqByte}|{SecretKey}";
			byte[] hashBytes = GetHashSha256(textToHash);
			byte[] sigBytes = new byte[3];
			Array.Copy(hashBytes, 0, sigBytes, 0, 3);

			// 5. Gom nhóm thành mảng 10 bytes
			byte[] payload = new byte[10];
			Array.Copy(daysBytes, 0, payload, 0, 2);
			Array.Copy(shortHwIdBytes, 0, payload, 2, 4);
			payload[6] = seqByte;
			Array.Copy(sigBytes, 0, payload, 7, 3);

			// 6. Mã hóa sang Base32 (16 ký tự)
			string rawBase32 = Base32.Encode(payload);

			// 7. Format thành XXXX-XXXX-XXXX-XXXX
			StringBuilder formatted = new StringBuilder();
			for (int i = 0; i < 16; i++)
			{
				if (i > 0 && i % 4 == 0)
				{
					formatted.Append("-");
				}
				formatted.Append(rawBase32[i]);
			}
			return formatted.ToString();
		}

		private static string ComputeSHA256String(string text)
		{
			using SHA256 sha256 = SHA256.Create();
			byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("X2"));
			return sb.ToString();
		}

		private static byte[] GetHashSha256(string text)
		{
			using SHA256 sha256 = SHA256.Create();
			return sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
		}

		private static string Encrypt(string plainText)
		{
			byte[] iv = new byte[16];
			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(SecretKey);
			aes.IV = iv; // For simplicity in this non-critical app
			ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

			using MemoryStream ms = new();
			using (CryptoStream cs = new(ms, encryptor, CryptoStreamMode.Write))
			{
				using StreamWriter sw = new(cs);
				sw.Write(plainText);
			}
			return Convert.ToBase64String(ms.ToArray());
		}

		private static string Decrypt(string cipherText)
		{
			byte[] iv = new byte[16];
			byte[] buffer = Convert.FromBase64String(cipherText);

			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(SecretKey);
			aes.IV = iv;
			ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

			using MemoryStream ms = new(buffer);
			using CryptoStream cs = new(ms, decryptor, CryptoStreamMode.Read);
			using StreamReader sr = new(cs);
			return sr.ReadToEnd();
		}
	}

	public static class Base32
	{
		private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

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

		public static byte[] Decode(string input)
		{
			StringBuilder sb = new StringBuilder();
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
