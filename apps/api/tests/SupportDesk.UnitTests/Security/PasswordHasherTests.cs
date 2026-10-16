using SupportDesk.Presentation.Security;

namespace SupportDesk.UnitTests.Security;

public class PasswordHasherTests
{
    private readonly AspNetPasswordHasher _hasher = new();

    [Fact]
    public void VerifyPassword_CorrectPassword_Succeeds()
    {
        var password = "DevPassword123!";
        var hash = _hasher.HashPassword(password);

        var isValid = _hasher.VerifyPassword(hash, password);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("WrongPassword!")]
    [InlineData("devpassword123!")]
    [InlineData("DevPassword123")]
    [InlineData(" ")]
    public void VerifyPassword_IncorrectPassword_Fails(string incorrectPassword)
    {
        var password = "DevPassword123!";
        var hash = _hasher.HashPassword(password);

        var isValid = _hasher.VerifyPassword(hash, incorrectPassword);

        Assert.False(isValid);
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashesDueToSalting()
    {
        var password = "DevPassword123!";
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        Assert.True(_hasher.VerifyPassword(hash1, password));
        Assert.True(_hasher.VerifyPassword(hash2, password));
    }

    [Theory]
    [InlineData(null, "DevPassword123!")]
    [InlineData("", "DevPassword123!")]
    [InlineData("SomeHash", null)]
    [InlineData("SomeHash", "")]
    public void VerifyPassword_NullOrEmptyInputs_ReturnsFalse(string? hash, string? password)
    {
        var isValid = _hasher.VerifyPassword(hash!, password!);
        Assert.False(isValid);
    }
}
