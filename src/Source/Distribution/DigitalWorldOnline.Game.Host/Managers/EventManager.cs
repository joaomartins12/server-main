using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Arena;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Game.Managers
{
    public class EventManager
    {
        private readonly AssetsLoader _assets;

        public EventManager(AssetsLoader assets)
        {
            _assets = assets;
        }
        // private DateTime roundStartTime;
        //     private DateTime roundCooldownStartTime;
        //    private const int roundDurationInSeconds = 180; // TIME PER ROUND
        //     private const int roundCooldownDuration = 40; // COOLDOWN PER ROUND (mob duration should NEVER BE HIGHER than cooldown of a round)
        //     private int currentRound = 1;
        //     private bool roundMessageSent = false;
        //     private const int restartCooldownDuration = 300; // Cooldown after all rounds
        //     private const int summonCooldownDuration = 7; // RESPAWN MOB TIME
        //     private bool restartMessageSent = false;
        //    private DateTime lastSummonTime = DateTime.MinValue;
        //     private bool roundEndMessageSent = false;
        //    private DateTime roundEndCooldownStartTime = DateTime.MinValue;
        //    private bool eventStartMessageSent = false;
        //    private static DateTime currentTime => DateTime.Now;
    }
}


/* public readonly (int hour, int minute)[] eventTimes =
   {
         (0, 0),
         (2, 30),
         (5, 00),
         (7, 30),
         (10, 00),
         (12, 30),
         (15, 00),
         (17, 30),
         (20, 00),
         (22, 30)
     };



 public async Task EventServer(MapServer map,DungeonsServer map2,EventServer map3)
 {

     var eventTime = eventTimes
              .FirstOrDefault(t =>
              {
                  DateTime eventStart = new DateTime(currentTime.Year,currentTime.Month,currentTime.Day,t.hour,t.minute,0);
                  DateTime eventEnd = eventStart.AddMinutes(17);
                  return currentTime >= eventStart && currentTime <= eventEnd;
              });

     if (eventTime != default)
     {

         var mobsList = new Dictionary<int,SummonModel?>
         {
             { 1, _assets.SummonInfo.FirstOrDefault(x => x.Id == 138) },
             { 2, _assets.SummonInfo.FirstOrDefault(x => x.Id == 138) },
             { 3, _assets.SummonInfo.FirstOrDefault(x => x.Id == 138) },
             { 4, _assets.SummonInfo.FirstOrDefault(x => x.Id == 138) }
             //{ 5, _assets.SummonInfo.FirstOrDefault(x => x.Id == 81) }
         };

         if (roundCooldownStartTime != DateTime.MinValue &&
             (DateTime.Now - roundCooldownStartTime).TotalSeconds < roundCooldownDuration)
         {
             return;
         }

         if (currentRound > mobsList.Count)
         {
             if (!restartMessageSent)
             {

                 map.BroadcastGlobal(new NoticeMessagePacket("All rounds completed. Restarting in 2.30 hours...").Serialize());
                 map2.BroadcastGlobal(new NoticeMessagePacket($"All rounds completed. Restarting in in 2.30 hours...").Serialize());
                 map3.BroadcastGlobal(new NoticeMessagePacket($"All rounds completed. Restarting in in 2.30 hours...").Serialize());

                 restartMessageSent = true;
                 eventStartMessageSent = false;

             }

             roundCooldownStartTime = DateTime.Now;
             _ = Task.Run(async () =>
             {
                 await Task.Delay(restartCooldownDuration * 1000);

                 lock (this)
                 {
                     currentRound = 1;
                     restartMessageSent = false;
                     roundEndMessageSent = false;
                     roundStartTime = DateTime.MinValue;
                     lastSummonTime = DateTime.MinValue;
                     roundMessageSent = false;
                 }
             });

             return;
         }

         if (currentRound == 1 && !eventStartMessageSent)
         {
             map.BroadcastGlobal(new NoticeMessagePacket("The event has started!").Serialize());
             map2.BroadcastGlobal(new NoticeMessagePacket("The event has started!").Serialize());

             eventStartMessageSent = true;

             await Task.Delay(4000);
             await StartCountdown(map,map2,map3,3);

             //map.BroadcastGlobal(new NoticeMessagePacket($"Round start {currentRound}").Serialize());
             roundMessageSent = true;
         }

         if (roundStartTime == DateTime.MinValue)
         {
             roundStartTime = DateTime.Now;
         }

         if ((DateTime.Now - roundStartTime).TotalSeconds >= roundDurationInSeconds)
         {
             if (currentRound < mobsList.Count)
             {
                 if (!roundEndMessageSent)
                 {
                     map.BroadcastGlobal(new NoticeMessagePacket($"Round {currentRound} ended. Next round will start in {roundCooldownDuration} seconds").Serialize());
                     map2.BroadcastGlobal(new NoticeMessagePacket($"Round {currentRound} ended. Next round will start in {roundCooldownDuration} seconds").Serialize());
                     map3.BroadcastGlobal(new NoticeMessagePacket($"Round {currentRound} ended. Next round will start in {roundCooldownDuration} seconds").Serialize());

                     roundEndMessageSent = true;


                     roundEndCooldownStartTime = DateTime.Now;
                     await Task.Delay(300);

                     await StartCountdown(map,map2,map3,roundCooldownDuration);


                 }

                 if ((DateTime.Now - roundEndCooldownStartTime).TotalSeconds >= 15)
                 {
                     currentRound++;
                     roundMessageSent = false;
                     roundEndMessageSent = false;
                     roundStartTime = DateTime.Now;
                     lastSummonTime = DateTime.MinValue;
                     roundEndCooldownStartTime = DateTime.MinValue;
                 }
                 return;
             }
             else
             {
                 currentRound++;
                 roundMessageSent = false;
                 roundEndMessageSent = false;
                 roundStartTime = DateTime.Now;
                 lastSummonTime = DateTime.MinValue;
                 roundEndCooldownStartTime = DateTime.MinValue;
                 return;
             }
         }

         if (!roundMessageSent && mobsList.ContainsKey(currentRound))
         {
             if (currentRound == 1)
             {
                 return;
             }
             else
                 //map.BroadcastGlobal(new DungeonArenaNextStagePacket(0,0,1).Serialize());

                 //map.BroadcastGlobal(new NoticeMessagePacket($"Round start {currentRound}").Serialize());
                 roundMessageSent = true;
         }

         if (mobsList.TryGetValue(currentRound,out var mobs) && mobs != null)
         {
             if (lastSummonTime == DateTime.MinValue || (DateTime.Now - lastSummonTime).TotalSeconds >= summonCooldownDuration)
             {
                 await SummonMobs(mobs,map3);
                 lastSummonTime = DateTime.Now;
             }
         }
     }
 }
*/

/*  private async Task SummonMobs(SummonModel summonInfo,EventServer maps)
 {
     foreach (var mobToAdd in summonInfo.SummonedMobs)
     {
         var mob = (SummonMobModel)mobToAdd.Clone();
         var matchingMaps = maps.Maps.Where(x => x.MapId == mob.Location.MapId).ToList();  // Make a copy

         foreach (var map in matchingMaps)
         {
             if (map.SummonMobs.Any(existingMob => existingMob.Id == mob.Id))
             {
                 continue;
             }

             if (matchingMaps == null) return; // This check is redundant and can be removed.

             mob.TamersViewing.Clear();
             mob.Reset();
             mob.SetRespawn();
             mob.SetId(mob.Id);
             mob.SetLocation(mob.Location.MapId,mob.Location.X,mob.Location.Y);
             mob.SetDuration();

             // Instead of modifying directly, collect mobs first.
             var mobsToAdd = new List<SummonMobModel>();
             mobsToAdd.Add(mob);

             // Add after iteration
             foreach (var m in mobsToAdd)
             {
                 maps.AddSummonMobs(m.Location.MapId,m);
             }
         }

     }
 }*/
/*  private async Task StartCountdown(MapServer map,DungeonsServer map2,EventServer map3,int timer)
 {
     var time = timer * 1000;

     map.BroadcastGlobal(new DungeonArenaNextStagePacket(0,0,time).Serialize());
     map2.BroadcastGlobal(new DungeonArenaNextStagePacket(0,0,time).Serialize());
     map3.BroadcastGlobal(new DungeonArenaNextStagePacket(0,0,time).Serialize());



     await Task.Delay(time);

 }

}
}*/
