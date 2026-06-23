using EvaluSystemBack.Dtos;

namespace EvaluSystemBack.Services.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}
