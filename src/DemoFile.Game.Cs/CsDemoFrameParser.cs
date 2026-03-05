using DemoFile.Game.Cs;
using Google.Protobuf;

namespace DemoFile;

/// <summary>
/// A Counter-Strike 2 specific demo frame parser that extends <see cref="DemoFrameParser"/>
/// with parsing of CS2-specific network messages (game events, user messages).
/// </summary>
public sealed class CsDemoFrameParser : DemoFrameParser
{
    /// <summary>
    /// Construct a new CS2 <c>.dem</c> frame parser.
    /// </summary>
    /// <param name="stream">A stream of the <c>.dem</c> file.</param>
    public CsDemoFrameParser(Stream stream) : base(stream)
    {
    }

    /// <inheritdoc />
    protected override (string Name, IMessage? Body) ParseSingleNetworkMessage(int msgType, ReadOnlySpan<byte> buf)
    {
        // CS-specific game events
        switch (msgType)
        {
            case (int)ECsgoGameEvents.GePlayerAnimEventId:
                return (nameof(ECsgoGameEvents.GePlayerAnimEventId), CMsgTEPlayerAnimEvent.Parser.ParseFrom(buf));
            case (int)ECsgoGameEvents.GeRadioIconEventId:
                return (nameof(ECsgoGameEvents.GeRadioIconEventId), CMsgTERadioIcon.Parser.ParseFrom(buf));
            case (int)ECsgoGameEvents.GeFireBulletsId:
                return (nameof(ECsgoGameEvents.GeFireBulletsId), CMsgTEFireBullets.Parser.ParseFrom(buf));
        }

        // CS-specific user messages
        switch (msgType)
        {
            case (int)ECstrike15UserMessages.CsUmVguimenu:
                return (nameof(ECstrike15UserMessages.CsUmVguimenu), CCSUsrMsg_VGUIMenu.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmGeiger:
                return (nameof(ECstrike15UserMessages.CsUmGeiger), CCSUsrMsg_Geiger.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmTrain:
                return (nameof(ECstrike15UserMessages.CsUmTrain), CCSUsrMsg_Train.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmHudText:
                return (nameof(ECstrike15UserMessages.CsUmHudText), CCSUsrMsg_HudText.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmHudMsg:
                return (nameof(ECstrike15UserMessages.CsUmHudMsg), CCSUsrMsg_HudMsg.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmResetHud:
                return (nameof(ECstrike15UserMessages.CsUmResetHud), CCSUsrMsg_ResetHud.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmGameTitle:
                return (nameof(ECstrike15UserMessages.CsUmGameTitle), CCSUsrMsg_GameTitle.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmShake:
                return (nameof(ECstrike15UserMessages.CsUmShake), CCSUsrMsg_Shake.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmFade:
                return (nameof(ECstrike15UserMessages.CsUmFade), CCSUsrMsg_Fade.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRumble:
                return (nameof(ECstrike15UserMessages.CsUmRumble), CCSUsrMsg_Rumble.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmCloseCaption:
                return (nameof(ECstrike15UserMessages.CsUmCloseCaption), CCSUsrMsg_CloseCaption.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmCloseCaptionDirect:
                return (nameof(ECstrike15UserMessages.CsUmCloseCaptionDirect), CCSUsrMsg_CloseCaptionDirect.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSendAudio:
                return (nameof(ECstrike15UserMessages.CsUmSendAudio), CCSUsrMsg_SendAudio.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRawAudio:
                return (nameof(ECstrike15UserMessages.CsUmRawAudio), CCSUsrMsg_RawAudio.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmVoiceMask:
                return (nameof(ECstrike15UserMessages.CsUmVoiceMask), CCSUsrMsg_VoiceMask.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRequestState:
                return (nameof(ECstrike15UserMessages.CsUmRequestState), CCSUsrMsg_RequestState.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmDamage:
                return (nameof(ECstrike15UserMessages.CsUmDamage), CCSUsrMsg_Damage.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRadioText:
                return (nameof(ECstrike15UserMessages.CsUmRadioText), CCSUsrMsg_RadioText.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmHintText:
                return (nameof(ECstrike15UserMessages.CsUmHintText), CCSUsrMsg_HintText.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmKeyHintText:
                return (nameof(ECstrike15UserMessages.CsUmKeyHintText), CCSUsrMsg_KeyHintText.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmProcessSpottedEntityUpdate:
                return (nameof(ECstrike15UserMessages.CsUmProcessSpottedEntityUpdate), CCSUsrMsg_ProcessSpottedEntityUpdate.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmReloadEffect:
                return (nameof(ECstrike15UserMessages.CsUmReloadEffect), CCSUsrMsg_ReloadEffect.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmAdjustMoney:
                return (nameof(ECstrike15UserMessages.CsUmAdjustMoney), CCSUsrMsg_AdjustMoney.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmStopSpectatorMode:
                return (nameof(ECstrike15UserMessages.CsUmStopSpectatorMode), CCSUsrMsg_StopSpectatorMode.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmKillCam:
                return (nameof(ECstrike15UserMessages.CsUmKillCam), CCSUsrMsg_KillCam.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmDesiredTimescale:
                return (nameof(ECstrike15UserMessages.CsUmDesiredTimescale), CCSUsrMsg_DesiredTimescale.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmCurrentTimescale:
                return (nameof(ECstrike15UserMessages.CsUmCurrentTimescale), CCSUsrMsg_CurrentTimescale.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmAchievementEvent:
                return (nameof(ECstrike15UserMessages.CsUmAchievementEvent), CCSUsrMsg_AchievementEvent.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmMatchEndConditions:
                return (nameof(ECstrike15UserMessages.CsUmMatchEndConditions), CCSUsrMsg_MatchEndConditions.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmDisconnectToLobby:
                return (nameof(ECstrike15UserMessages.CsUmDisconnectToLobby), CCSUsrMsg_DisconnectToLobby.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmPlayerStatsUpdate:
                return (nameof(ECstrike15UserMessages.CsUmPlayerStatsUpdate), CCSUsrMsg_PlayerStatsUpdate.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmWarmupHasEnded:
                return (nameof(ECstrike15UserMessages.CsUmWarmupHasEnded), CCSUsrMsg_WarmupHasEnded.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmClientInfo:
                return (nameof(ECstrike15UserMessages.CsUmClientInfo), CCSUsrMsg_ClientInfo.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmXrankGet:
                return (nameof(ECstrike15UserMessages.CsUmXrankGet), CCSUsrMsg_XRankGet.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmXrankUpd:
                return (nameof(ECstrike15UserMessages.CsUmXrankUpd), CCSUsrMsg_XRankUpd.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmCallVoteFailed:
                return (nameof(ECstrike15UserMessages.CsUmCallVoteFailed), CCSUsrMsg_CallVoteFailed.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmVoteStart:
                return (nameof(ECstrike15UserMessages.CsUmVoteStart), CCSUsrMsg_VoteStart.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmVotePass:
                return (nameof(ECstrike15UserMessages.CsUmVotePass), CCSUsrMsg_VotePass.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmVoteFailed:
                return (nameof(ECstrike15UserMessages.CsUmVoteFailed), CCSUsrMsg_VoteFailed.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmVoteSetup:
                return (nameof(ECstrike15UserMessages.CsUmVoteSetup), CCSUsrMsg_VoteSetup.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmServerRankRevealAll:
                return (nameof(ECstrike15UserMessages.CsUmServerRankRevealAll), CCSUsrMsg_ServerRankRevealAll.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSendLastKillerDamageToClient:
                return (nameof(ECstrike15UserMessages.CsUmSendLastKillerDamageToClient), CCSUsrMsg_SendLastKillerDamageToClient.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmServerRankUpdate:
                return (nameof(ECstrike15UserMessages.CsUmServerRankUpdate), CCSUsrMsg_ServerRankUpdate.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmItemPickup:
                return (nameof(ECstrike15UserMessages.CsUmItemPickup), CCSUsrMsg_ItemPickup.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmShowMenu:
                return (nameof(ECstrike15UserMessages.CsUmShowMenu), CCSUsrMsg_ShowMenu.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmBarTime:
                return (nameof(ECstrike15UserMessages.CsUmBarTime), CCSUsrMsg_BarTime.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmAmmoDenied:
                return (nameof(ECstrike15UserMessages.CsUmAmmoDenied), CCSUsrMsg_AmmoDenied.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmMarkAchievement:
                return (nameof(ECstrike15UserMessages.CsUmMarkAchievement), CCSUsrMsg_MarkAchievement.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmMatchStatsUpdate:
                return (nameof(ECstrike15UserMessages.CsUmMatchStatsUpdate), CCSUsrMsg_MatchStatsUpdate.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmItemDrop:
                return (nameof(ECstrike15UserMessages.CsUmItemDrop), CCSUsrMsg_ItemDrop.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmGlowPropTurnOff:
                return (nameof(ECstrike15UserMessages.CsUmGlowPropTurnOff), CCSUsrMsg_GlowPropTurnOff.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSendPlayerItemDrops:
                return (nameof(ECstrike15UserMessages.CsUmSendPlayerItemDrops), CCSUsrMsg_SendPlayerItemDrops.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRoundBackupFilenames:
                return (nameof(ECstrike15UserMessages.CsUmRoundBackupFilenames), CCSUsrMsg_RoundBackupFilenames.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSendPlayerItemFound:
                return (nameof(ECstrike15UserMessages.CsUmSendPlayerItemFound), CCSUsrMsg_SendPlayerItemFound.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmReportHit:
                return (nameof(ECstrike15UserMessages.CsUmReportHit), CCSUsrMsg_ReportHit.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmXpUpdate:
                return (nameof(ECstrike15UserMessages.CsUmXpUpdate), CCSUsrMsg_XpUpdate.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmQuestProgress:
                return (nameof(ECstrike15UserMessages.CsUmQuestProgress), CCSUsrMsg_QuestProgress.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmScoreLeaderboardData:
                return (nameof(ECstrike15UserMessages.CsUmScoreLeaderboardData), CCSUsrMsg_ScoreLeaderboardData.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmPlayerDecalDigitalSignature:
                return (nameof(ECstrike15UserMessages.CsUmPlayerDecalDigitalSignature), CCSUsrMsg_PlayerDecalDigitalSignature.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmWeaponSound:
                return (nameof(ECstrike15UserMessages.CsUmWeaponSound), CCSUsrMsg_WeaponSound.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmUpdateScreenHealthBar:
                return (nameof(ECstrike15UserMessages.CsUmUpdateScreenHealthBar), CCSUsrMsg_UpdateScreenHealthBar.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmEntityOutlineHighlight:
                return (nameof(ECstrike15UserMessages.CsUmEntityOutlineHighlight), CCSUsrMsg_EntityOutlineHighlight.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSsui:
                return (nameof(ECstrike15UserMessages.CsUmSsui), CCSUsrMsg_SSUI.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmSurvivalStats:
                return (nameof(ECstrike15UserMessages.CsUmSurvivalStats), CCSUsrMsg_SurvivalStats.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmEndOfMatchAllPlayersData:
                return (nameof(ECstrike15UserMessages.CsUmEndOfMatchAllPlayersData), CCSUsrMsg_EndOfMatchAllPlayersData.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmPostRoundDamageReport:
                return (nameof(ECstrike15UserMessages.CsUmPostRoundDamageReport), CCSUsrMsg_PostRoundDamageReport.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmRoundEndReportData:
                return (nameof(ECstrike15UserMessages.CsUmRoundEndReportData), CCSUsrMsg_RoundEndReportData.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmCurrentRoundOdds:
                return (nameof(ECstrike15UserMessages.CsUmCurrentRoundOdds), CCSUsrMsg_CurrentRoundOdds.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmDeepStats:
                return (nameof(ECstrike15UserMessages.CsUmDeepStats), CCSUsrMsg_DeepStats.Parser.ParseFrom(buf));
            case (int)ECstrike15UserMessages.CsUmShootInfo:
                return (nameof(ECstrike15UserMessages.CsUmShootInfo), CCSUsrMsg_ShootInfo.Parser.ParseFrom(buf));
        }

        // Fall back to base parser for common NET/SVC messages
        return base.ParseSingleNetworkMessage(msgType, buf);
    }
}
