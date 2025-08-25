using System.Threading.Tasks;
using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Application.Ports;

public interface IPoolStateReader
{
    Task<PoolStateDto> GetStateAsync();
}
