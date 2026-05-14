namespace MT.Models;

/// <summary>登入表單（Login.razor）。</summary>
public sealed class LoginFormModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string CaptchaInput { get; set; } = "";
}

/// <summary>首次登入強制改密碼表單（FirstLoginPassword.razor）。</summary>
public sealed class FirstLoginPasswordFormModel
{
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>忘記密碼 Token 後的重設密碼表單（ResetPassword.razor）。</summary>
public sealed class ResetPasswordFormModel
{
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
}
