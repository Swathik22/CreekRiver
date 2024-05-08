using System.ComponentModel.DataAnnotations;

namespace CreekRiver.Models.DTOs;

public class CampsiteDTO
{
    public int Id { get; set; }
    [Required]
    public string Nickname { get; set; }
    public string ImageUrl { get; set; }
    public int CampsiteTypeId { get; set; }
    public CampsiteTypeDTO CampsiteType { get; set; }
    public List<ReservationDTO> Reservations { get; set; }
}