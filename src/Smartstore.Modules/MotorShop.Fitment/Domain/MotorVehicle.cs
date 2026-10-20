using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Smartstore.Domain;

namespace MotorShop.Fitment.Domain;

/// <summary>
/// A motorcycle model produced within a range of years, e.g. "Honda CBR 250R 2011-2013".
/// </summary>
[Table("MotorVehicle")]
[Index(nameof(Make), nameof(Model), Name = "IX_MotorVehicle_Make_Model")]
public class MotorVehicle : BaseEntity
{
    [Required, StringLength(100)]
    public string Make { get; set; }

    [Required, StringLength(200)]
    public string Model { get; set; }

    public int YearFrom { get; set; }

    public int YearTo { get; set; }

    public int? EngineCc { get; set; }

    /// <summary>
    /// Lower bound of the year range. A range stored with YearTo &lt; YearFrom is read as [YearTo, YearFrom].
    /// </summary>
    [NotMapped]
    public int FirstYear => Math.Min(YearFrom, YearTo);

    /// <summary>
    /// Upper bound of the year range. A range stored with YearTo &lt; YearFrom is read as [YearTo, YearFrom].
    /// </summary>
    [NotMapped]
    public int LastYear => Math.Max(YearFrom, YearTo);

    [NotMapped]
    public string DisplayName
        => $"{Make} {Model} ({(FirstYear == LastYear ? FirstYear.ToString() : $"{FirstYear}-{LastYear}")})";

    public bool CoversYear(int year)
        => year >= FirstYear && year <= LastYear;
}
