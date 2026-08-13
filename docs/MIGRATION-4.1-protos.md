# Migrating to CS2OpenDev.Protos 4.1

`CS2OpenDev.Protos` goes 3.0.6 → 4.1.0. **188 top-level types are removed** and
three `.proto` files are gone from the package. Nothing is renamed, nothing
changes shape, and no type is added.

> **If you have 3.0.7, you already have this break.** The unattended refresh
> picked up the tracker bump and shipped the removal as a patch before the major
> was corrected, so `CS2OpenDev.Protos` 3.0.7 contains the pruned closure while
> its version number promises compatibility with 3.0.6. 4.1.0 is the same
> content under a version that states what happened. Treat 3.0.7 as withdrawn
> and move to 4.1.0; there is no 3.0.x that both post-dates the prune and is
> safe to pin.

Measured against SchemaTracker `859f2e8d` (v1.3.0), CS2 build `24701871`.
Regenerate the inventory with:

```
python3 scripts/normalize-protos.py && git diff --stat -- protos/
```

## Why this is a major bump

The removal did not originate here, and it is not a Valve change either. As of
SchemaTracker v1.3.0 the extractor emits `cstrike15_gcmessages.proto` as a
**derived closure** rather than the full embedded file: it keeps the top-level
types transitively referenced by the rest of the artifact set and drops the
rest, re-deriving on every extract so a new referencing field re-grows the
closure automatically. The file's own header records the result for the build it
came from — `17 of 162 top-level types kept`.

Three imports became unnecessary once the surviving closure no longer referenced
them, so they are no longer staged into `protos/` at all:

| File | Top-level types | Status |
|---|---|---|
| `cstrike15_gcmessages.proto` | 162 → 17 | pruned, 145 removed |
| `gcsdk_gcmessages.proto` | 38 | removed entirely |
| `steammessages.proto` | 4 | removed entirely |
| `engine_gcmessages.proto` | 1 | removed entirely |

What went is Steam Game-Coordinator traffic — matchmaking, party, inventory,
item schema, Overwatch, tournament and store messages. None of it crosses the
demo or network wire path that this package exists to serve, which is why the
closure never referenced it.

The SDK itself is unaffected: the entire `CS2OpenDev.Sdk` diff for this build is
a two-value `MGetKV3ClassDefaults` metadata edit on `DynPitchVol` and
`DynPitchVolBase` (`vol` 32762 → 32760). `Sdk` and `Sdk.GameEvents` stay at 4.1
rather than taking a major they did not have.

Protos was already a major behind — `Sdk` and `Sdk.GameEvents` moved to 4.x
while this package stayed at 3.0, against the rule in the root README that the
major is kept in step by hand. Rejoining at 4.1 fixes that drift and signals the
break in the same motion, which is the whole job `MAJOR` has here.

Nothing in the pipeline catches this on its own, which is how 3.0.7 happened.
Build, tests, the readiness gate and the regen fixed point were all green — none
of them is a semantic-versioning gate, and none claims to be. The major comes
from a human editing `version.json`; the patch comes from git height. When
upstream removes public API, only the first of those notices.

## If you only parse demos, you are not affected

Nothing on the demo path moved. `demo.proto`, `netmessages.proto`,
`gameevents.proto`, `cs_gameevents.proto`, `usermessages.proto`,
`cstrike15_usermessages.proto`, `usercmd.proto`, `cs_usercmd.proto`,
`te.proto`, `networkbasetypes.proto`, `network_connection.proto`,
`clientmessages.proto`, `connectionless_netmessages.proto`,
`source2_steam_stats.proto` and `valveextensions.proto` are all still staged and
unchanged in shape. The package still compiles as a closed set — `protoc`
verifies the 15 surviving files in isolation on every regen.

### Four removals that look demo-adjacent and are not

Worth naming, because a demo consumer scanning the list will stop on these:

| Type | What it actually is |
|---|---|
| `CMsgGCCStrike15_GotvSyncPacket` | GC-side GOTV session bookkeeping, not the GOTV/HLTV wire format |
| `CEngineGotvSyncPacket` | the same, engine side of that GC exchange |
| `ServerHltvInfo` | a field block inside GC match-list replies |
| `WatchableMatchInfo` | GC "what can I watch" metadata, from the match-list path |

Broadcast and GOTV demo playback are carried by `demo.proto` and
`netmessages.proto`, neither of which is touched.

One name collision to be aware of: `CVDiagnostic` appears in the removed list,
but `usermessages.proto` defines its own nested
`CUserMessage_DllStatus.CVDiagnostic` and refers to it fully qualified. That
type survives; only the unrelated top-level one in `cstrike15_gcmessages.proto`
is gone.

## If you use the Game Coordinator types

They are not coming back to this package. The closure is re-derived on every
extract from what the artifact set references, so a GC message returns only if
something on the wire path starts referencing it.

Get them from the full embedded descriptors instead — SchemaTracker publishes
the complete, unpruned `.proto` set per build under
`artifacts/<build>/<platform>/protos/`, and `protos.descriptorset` alongside it.
That is the same source this package is staged from; `protos/PROVENANCE.json`
records the exact build and commit each release was cut from.

## Removed types

`engine_gcmessages.proto` (1)

`CEngineGotvSyncPacket`

`steammessages.proto` (4)

`CChinaAgreementSessions_StartAgreementSessionInGame_Request`,
`CChinaAgreementSessions_StartAgreementSessionInGame_Response`,
`CMsgProtoBufHeader`, `GCProtoBufMsgSrc`

`gcsdk_gcmessages.proto` (38)

`CGCToGCMsgMasterAck`, `CGCToGCMsgMasterAck_Response`,
`CGCToGCMsgMasterStartupComplete`, `CGCToGCMsgRouted`, `CGCToGCMsgRoutedReply`,
`CGameServers_AggregationQuery_Request`,
`CGameServers_AggregationQuery_Response`, `CMsgAccountDetails`,
`CMsgClientHello`, `CMsgClientWelcome`, `CMsgConnectionStatus`,
`CMsgGCMultiplexMessage`, `CMsgGCMultiplexMessage_Response`,
`CMsgGCRequestSessionIP`, `CMsgGCRequestSessionIPResponse`,
`CMsgGCUpdateSessionIP`, `CMsgSOCacheHaveVersion`, `CMsgSOCacheSubscribed`,
`CMsgSOCacheSubscriptionCheck`, `CMsgSOCacheSubscriptionRefresh`,
`CMsgSOCacheUnsubscribed`, `CMsgSOCacheVersion`, `CMsgSOIDOwner`,
`CMsgSOMultipleObjects`, `CMsgSOSingleObject`, `CMsgSerializedSOCache`,
`CMsgServerHello`, `CProductInfo_SetRichPresenceLocalization_Request`,
`CProductInfo_SetRichPresenceLocalization_Response`,
`CWorkshop_AddSpecialPayment_Request`, `CWorkshop_AddSpecialPayment_Response`,
`CWorkshop_GetContributors_Request`, `CWorkshop_GetContributors_Response`,
`CWorkshop_PopulateItemDescriptions_Request`,
`CWorkshop_SetItemPaymentRules_Request`,
`CWorkshop_SetItemPaymentRules_Response`, `GCClientLauncherType`,
`GCConnectionStatus`

`cstrike15_gcmessages.proto` (145 of 162; the 17 kept are listed after)

`AccountActivity`, `CAttribute_String`, `CClientHeaderOverwatchEvidence`,
`CDataGCCStrike15_v2_MatchInfo`, `CDataGCCStrike15_v2_TournamentGroup`,
`CDataGCCStrike15_v2_TournamentGroupTeam`,
`CDataGCCStrike15_v2_TournamentInfo`, `CDataGCCStrike15_v2_TournamentSection`,
`CMsgCStrike15Welcome`, `CMsgCsgoSteamUserStatChange`,
`CMsgGCCStrike15_GotvSyncPacket`,
`CMsgGCCStrike15_v2_AccountPrivacySettings`,
`CMsgGCCStrike15_v2_Account_RequestCoPlays`,
`CMsgGCCStrike15_v2_AcknowledgePenalty`, `CMsgGCCStrike15_v2_BetaEnrollment`,
`CMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockRequest`,
`CMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockResponse`,
`CMsgGCCStrike15_v2_Client2GCRequestPrestigeCoin`,
`CMsgGCCStrike15_v2_Client2GCStreamUnlock`,
`CMsgGCCStrike15_v2_Client2GCTextMsg`,
`CMsgGCCStrike15_v2_Client2GcAckXPShopTracks`,
`CMsgGCCStrike15_v2_ClientAccountBalance`,
`CMsgGCCStrike15_v2_ClientAuthKeyCode`,
`CMsgGCCStrike15_v2_ClientCommendPlayer`,
`CMsgGCCStrike15_v2_ClientGCRankUpdate`,
`CMsgGCCStrike15_v2_ClientLogonFatalError`,
`CMsgGCCStrike15_v2_ClientNetworkConfig`,
`CMsgGCCStrike15_v2_ClientPartyJoinRelay`,
`CMsgGCCStrike15_v2_ClientPartyWarning`,
`CMsgGCCStrike15_v2_ClientPerfReport`,
`CMsgGCCStrike15_v2_ClientPlayerDecalSign`,
`CMsgGCCStrike15_v2_ClientPollState`, `CMsgGCCStrike15_v2_ClientReportPlayer`,
`CMsgGCCStrike15_v2_ClientReportResponse`,
`CMsgGCCStrike15_v2_ClientReportServer`,
`CMsgGCCStrike15_v2_ClientRequestJoinFriendData`,
`CMsgGCCStrike15_v2_ClientRequestJoinServerData`,
`CMsgGCCStrike15_v2_ClientRequestOffers`,
`CMsgGCCStrike15_v2_ClientRequestPlayersProfile`,
`CMsgGCCStrike15_v2_ClientRequestSouvenir`,
`CMsgGCCStrike15_v2_ClientRequestWatchInfoFriends`,
`CMsgGCCStrike15_v2_ClientSubmitSurveyVote`,
`CMsgGCCStrike15_v2_ClientToGCChat`,
`CMsgGCCStrike15_v2_ClientToGCRequestElevate`,
`CMsgGCCStrike15_v2_ClientToGCRequestTicket`,
`CMsgGCCStrike15_v2_ClientVarValueNotificationInfo`,
`CMsgGCCStrike15_v2_Fantasy`, `CMsgGCCStrike15_v2_GC2ClientInitSystem`,
`CMsgGCCStrike15_v2_GC2ClientInitSystem_Response`,
`CMsgGCCStrike15_v2_GC2ClientNotifyXPShop`,
`CMsgGCCStrike15_v2_GC2ClientRefuseSecureMode`,
`CMsgGCCStrike15_v2_GC2ClientRequestValidation`,
`CMsgGCCStrike15_v2_GC2ClientTextMsg`,
`CMsgGCCStrike15_v2_GC2ClientTournamentInfo`,
`CMsgGCCStrike15_v2_GC2ServerReservationUpdate`,
`CMsgGCCStrike15_v2_GCToClientChat`,
`CMsgGCCStrike15_v2_GetEventFavorites_Request`,
`CMsgGCCStrike15_v2_GetEventFavorites_Response`,
`CMsgGCCStrike15_v2_GiftsLeaderboardRequest`,
`CMsgGCCStrike15_v2_GiftsLeaderboardResponse`,
`CMsgGCCStrike15_v2_MatchEndRewardDropsNotification`,
`CMsgGCCStrike15_v2_MatchEndRunRewardDrops`, `CMsgGCCStrike15_v2_MatchList`,
`CMsgGCCStrike15_v2_MatchListRequestCurrentLiveGames`,
`CMsgGCCStrike15_v2_MatchListRequestFullGameInfo`,
`CMsgGCCStrike15_v2_MatchListRequestLiveGameForUser`,
`CMsgGCCStrike15_v2_MatchListRequestRecentUserGames`,
`CMsgGCCStrike15_v2_MatchListRequestTournamentGames`,
`CMsgGCCStrike15_v2_MatchListTournamentOperatorMgmt`,
`CMsgGCCStrike15_v2_MatchmakingClient2GCHello`,
`CMsgGCCStrike15_v2_MatchmakingClient2ServerPing`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientAbandon`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientHello`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientReserve`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientSearchStats`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientUpdate`,
`CMsgGCCStrike15_v2_MatchmakingGC2ClientUpdate_Note`,
`CMsgGCCStrike15_v2_MatchmakingGC2ServerConfirm`,
`CMsgGCCStrike15_v2_MatchmakingOperator2GCBlogUpdate`,
`CMsgGCCStrike15_v2_MatchmakingServerReservationResponse`,
`CMsgGCCStrike15_v2_MatchmakingServerRoundStats`,
`CMsgGCCStrike15_v2_MatchmakingStart`, `CMsgGCCStrike15_v2_MatchmakingStop`,
`CMsgGCCStrike15_v2_Party_Invite`, `CMsgGCCStrike15_v2_Party_Register`,
`CMsgGCCStrike15_v2_Party_Search`, `CMsgGCCStrike15_v2_Party_SearchResults`,
`CMsgGCCStrike15_v2_PlayerOverwatchCaseAssignment`,
`CMsgGCCStrike15_v2_PlayerOverwatchCaseStatus`,
`CMsgGCCStrike15_v2_PlayerOverwatchCaseUpdate`,
`CMsgGCCStrike15_v2_PlayersProfile`, `CMsgGCCStrike15_v2_Predictions`,
`CMsgGCCStrike15_v2_PremierSeasonSummary`,
`CMsgGCCStrike15_v2_Server2GCClientValidate`,
`CMsgGCCStrike15_v2_ServerNotificationForUserPenalty`,
`CMsgGCCStrike15_v2_ServerVarValueNotificationInfo`,
`CMsgGCCStrike15_v2_SetClanId`, `CMsgGCCStrike15_v2_SetEventFavorite`,
`CMsgGCCStrike15_v2_SetPlayerLeaderboardSafeName`,
`CMsgGCCStrike15_v2_VolatileShopSubscribe`,
`CMsgGCCStrike15_v2_WatchInfoUsers`,
`CMsgGCCstrike15_v2_ClientRedeemFreeReward`,
`CMsgGCCstrike15_v2_ClientRedeemMissionReward`,
`CMsgGCToClientSteamDatagramTicket`, `CMsgGC_GlobalGame_Play`,
`CMsgGC_GlobalGame_Subscribe`, `CMsgGC_GlobalGame_Unsubscribe`,
`CMsgGC_ServerQuestUpdateData`, `CMsgItemAcknowledged`,
`CMsgLegacySource1ClientWelcome`, `CMsgRecurringMissionSchema`,
`CMsgRequestRecurringMissionSchedule`, `CSOAccountItemPersonalStore`,
`CSOAccountKeychainRemoveToolCharges`, `CSOAccountRecurringMission`,
`CSOAccountRecurringSubscription`, `CSOAccountSeasonalOperation`,
`CSOAccountXpShop`, `CSOAccountXpShopBids`, `CSOEconCoupon`,
`CSOGameAccountSteamChina`, `CSOPersonaDataPublic`, `CSOQuestProgress`,
`CSOVolatileItemClaimedRewards`, `CSOVolatileItemOffer`, `CVDiagnostic`,
`DataCenterPing`, `DetailedSearchStatistic`, `EClientReportingVersion`,
`ECsgoGCMsg`, `ECsgoSteamUserStat`, `EInitSystemResult`, `GameServerPing`,
`GlobalStatistics`, `MatchEndItemUpdates`, `OperationalStatisticDescription`,
`OperationalStatisticElement`, `OperationalStatisticsPacket`,
`PlayerCommendationInfo`, `PlayerMedalsInfo`, `PlayerQuestData`, `QuestType`,
`ServerHltvInfo`, `TournamentMatchSetup`, `WatchableMatchInfo`

### Kept in `cstrike15_gcmessages.proto` (17)

`CDataGCCStrike15_v2_TournamentMatchDraft`, `CEconItemPreviewDataBlock`,
`CMsgGCCStrike15_ClientDeepStats`,
`CMsgGCCStrike15_v2_MatchmakingGC2ServerReserve`,
`CMsgGCCstrike15_v2_GC2ServerNotifyXPRewarded`, `CPreMatchInfoData`,
`DeepPlayerMatchEvent`, `DeepPlayerStatsEntry`, `IpAddressMask`,
`OperationalVarValue`, `PlayerDecalDigitalSignature`, `PlayerRankingInfo`,
`ScoreLeaderboardData`, `TournamentEvent`, `TournamentPlayer`,
`TournamentTeam`, `XpProgressData`

## Note on `ECsgoGCMsg`

`CS2OpenSchema.Server.ECsgoGCMsg` in `CS2OpenDev.Sdk` is a **different type**
with the same name — it is projected from `cs2_schema.json`, not from the
`.proto` set, and it is unaffected. Only
`CS2OpenSchema.Protos.ECsgoGCMsg` is gone.
