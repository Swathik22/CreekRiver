using System.ComponentModel.DataAnnotations;

namespace CreekRiver.Models.DTOs;

public class CampsiteTypeDTO
{
    public int Id { get; set; }
    [Required]
    public string CampsiteTypeName { get; set; }
    public int MaxReservationDays { get; set; }
    public decimal FeePerNight { get; set; }
}