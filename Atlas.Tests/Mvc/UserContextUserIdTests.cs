using Atlas.Mvc;
using Atlas.Services.Jobs;
using Xunit;

namespace Atlas.Tests.Mvc;

/// <summary>A user's id is their auth id: without an explicit `userId` claim, UserContext.UserId is the auth subject.</summary>
public class UserContextUserIdTests
{
    [Fact]
    public void JobContext_sets_UserId_to_the_auth_user_id()
    {
        var uc = new UserContext(new JobContext("tenant-1", "firebase-uid-1"));
        Assert.Equal("firebase-uid-1", uc.AuthUserId);
        Assert.Equal("firebase-uid-1", uc.UserId);
    }

    [Fact]
    public void SetJobContext_sets_UserId_to_the_auth_user_id()
    {
        var uc = new UserContext();
        uc.SetJobContext(new JobContext("tenant-1", "firebase-uid-2"));
        Assert.Equal("firebase-uid-2", uc.UserId);
    }
}
