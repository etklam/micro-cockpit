using System.Security.Cryptography;
using System.Text;

public static class DisciplineSelector
{
    public static int SelectIndex(Guid userId, DateOnly localDate, int itemCount)
    {
        if (itemCount <= 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{localDate:yyyy-MM-dd}"));
        return (int)(BitConverter.ToUInt64(hash, 0) % (ulong)itemCount);
    }
}
