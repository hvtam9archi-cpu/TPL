using System;
using System.Collections.Generic;

namespace TPL.Domain.Models
{
	/// <summary>
	/// Thông tin bản quyền — Pure C#.
	/// Đọc/ghi qua ILicenseRepository (Infrastructure).
	/// </summary>
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
}
