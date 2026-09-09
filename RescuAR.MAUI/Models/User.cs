using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models
{
    [Table("users")]
    public class User : BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = string.Empty;
        
        [Column("username")]
        public string Username { get; set; } = string.Empty;
        
        [Column("first_name")]
        public string FirstName { get; set; } = string.Empty;
        
        [Column("middle_name")]
        public string MiddleName { get; set; } = string.Empty;
        
        [Column("last_name")]
        public string LastName { get; set; } = string.Empty;
        
        [Column("email")]
        public string Email { get; set; } = string.Empty;
        
        [Column("phone_number")]
        public string PhoneNumber { get; set; } = string.Empty;
        
        [Column("address")]
        public string Address { get; set; } = string.Empty;
        
        [Column("avatar_url")]
        public string AvatarUrl { get; set; } = string.Empty;

        [Column("emergency_contact1_name")]
        public string EmergencyContact1Name { get; set; } = string.Empty;
        
        [Column("emergency_contact1_phone")]
        public string EmergencyContact1Phone { get; set; } = string.Empty;
        
        [Column("emergency_contact2_name")]
        public string EmergencyContact2Name { get; set; } = string.Empty;
        
        [Column("emergency_contact2_phone")]
        public string EmergencyContact2Phone { get; set; } = string.Empty;

        [Column("health_card_number")]
        public string HealthCardNumber { get; set; } = string.Empty;
        
        [Column("blood_type")]
        public string BloodType { get; set; } = string.Empty;
        
        [Column("allergies")]
        public string Allergies { get; set; } = string.Empty;
        
        [Column("maintenance_medications")]
        public string MaintenanceMedications { get; set; } = string.Empty;
        
        [Column("average_blood_pressure")]
        public string AverageBloodPressure { get; set; } = string.Empty;
        
        [Column("disability_special_needs")]
        public string DisabilityOrSpecialNeeds { get; set; } = string.Empty;
        
        [Column("is_organ_donor")]
        public bool IsOrganDonor { get; set; }

        [Column("circle_id")]
        public string CircleId { get; set; } = string.Empty;
    }
}
