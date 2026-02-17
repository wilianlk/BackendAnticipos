namespace BackendAnticipos.Models
{
    public class ReasignarAprobadorRequest
    {
        public List<int> IdsAnticipo { get; set; } = new();
        public int NuevoAprobadorId { get; set; }
        public string NuevoCorreoAprobador { get; set; } = string.Empty;
        public bool ReenviarCorreo { get; set; } = true;
    }
}
