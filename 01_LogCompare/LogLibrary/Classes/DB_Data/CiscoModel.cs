using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogLibrary.Classes.DB_Data
{
    public class CiscoModel
    {
        [Required]
        public string ModelName { get; set; } = string.Empty;
    }
}
