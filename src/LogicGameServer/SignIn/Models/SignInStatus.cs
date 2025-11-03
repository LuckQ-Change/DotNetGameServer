using System.Collections.Generic;

namespace LogicServer.SignIn.Models;

public record SignInStatus(int CurrentDay, IReadOnlyList<SignInReward> Rewards);