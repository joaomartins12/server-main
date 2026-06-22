using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Mechanics;

namespace DigitalWorldOnline.Game.Managers
{
    public class PartyManager
    {
        private int _partyId;

        public List<GameParty> Parties { get; private set; }

        public PartyManager()
        {
            Parties = new();
        }

        public GameParty CreateParty(CharacterModel leader, CharacterModel member)
        {
            _partyId++;

            var party = GameParty.Create(_partyId, leader, member);
            Parties.Add(party);
            return party;
        }

        /*public GameParty? FindParty(long leaderOrMemberId)
        {
            var party = Parties.FirstOrDefault(x => x.Members.Values.Any(y => y.Id == leaderOrMemberId));
            if (party != null && party.Members.Count == 1)
            {
                RemoveParty(party.Id);
                return null;
            }

            return party;
        }*/

        public GameParty? FindParty(long leaderOrMemberId)
        {
            return Parties.FirstOrDefault(x => x.Members.Values.Any(y => y.Id == leaderOrMemberId));
        }


        // RemoveParty deve ser tratado explicitamente ao se desfazer ou quando o último membro sai

        public void RemovePartyIfLastMember(long partyId)

        {
            var party = Parties.FirstOrDefault(p => p.Id == partyId);

            if (party != null && party.Members.Count == 1)

            {
                RemoveParty(partyId);
            }

        }

        public bool IsMemberInParty(long leaderOrMemberId, long tamerId)

        {
            var party = FindParty(leaderOrMemberId);

            if (party == null)

                return false;

            return party.Members.Values.Any(x => x.Id == tamerId);
        }

        public void UpdatePartyVisibility(CharacterModel player)
        {
            var party = FindParty(player.Id);
            if (party == null) return;

            foreach (var member in party.Members.Values)
            {
                foreach (var otherMember in party.Members.Values.Where(m => m.Id != member.Id))
                {
                    // Simula o envio de pacotes para o cliente do membro
                    SendUnloadAndLoadPackets(member, otherMember);
                }
            }
        }

        private void SendUnloadAndLoadPackets(CharacterModel member, CharacterModel otherMember)
        {
            // Implementação simulada para envio de pacotes de upload/download
            Console.WriteLine($"Enviando paquetes de descarga para {member.Name} y carga para {otherMember.Name}");
        }


        public void RemoveParty(long partyId)
        {
            Parties.RemoveAll(x => x.Id == partyId);
        }
    }
}
