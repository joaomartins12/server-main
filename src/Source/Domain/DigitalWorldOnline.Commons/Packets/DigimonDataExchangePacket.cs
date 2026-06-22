using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class DigimonDataExchangePacket : PacketWriter
    {
        private const int PacketNumber = 3242;
        public DigimonDataExchangePacket(DigimonDataExchangeEnum nDataChangeType, int result, byte leftDigimonSlot, byte rightDigimonSlot, DigimonModel leftDigimon, DigimonModel rightDigimon)
        {
            Type(PacketNumber);
            WriteInt((int)nDataChangeType);
            WriteByte(leftDigimonSlot);
            WriteByte(rightDigimonSlot);
            WriteInt(result);
            switch (nDataChangeType)
            {
                case DigimonDataExchangeEnum.eDataChangeType_Size:
                    WriteByte((byte)leftDigimon.HatchGrade);
                    WriteShort(leftDigimon.Size);
                    WriteByte((byte)rightDigimon.HatchGrade);
                    WriteShort(rightDigimon.Size);
                    break;

                case DigimonDataExchangeEnum.eDataChangeType_Inchant:
                    //Left Digimon Slot
                    WriteShort(leftDigimon.Digiclone.CloneLevel);
                    WriteShort(leftDigimon.Digiclone.ATValue);
                    WriteShort(leftDigimon.Digiclone.BLValue);
                    WriteShort(leftDigimon.Digiclone.CTValue);
                    WriteShort(0); //Valor atual do as (versao 487 nao tem)
                    WriteShort(leftDigimon.Digiclone.EVValue);
                    WriteShort(0); //Valor atual do HT (versao 487 nao tem)
                    WriteShort(leftDigimon.Digiclone.HPValue);

                    WriteShort(leftDigimon.Digiclone.ATLevel);
                    WriteShort(leftDigimon.Digiclone.BLLevel);
                    WriteShort(leftDigimon.Digiclone.CTLevel);
                    WriteShort(0); //Level atual do as (versao 487 nao tem)
                    WriteShort(leftDigimon.Digiclone.EVLevel);
                    WriteShort(0); //Level atual do HT (versao 487 nao tem)
                    WriteShort(leftDigimon.Digiclone.HPLevel);
                    //Right Digimon Slot
                    WriteShort(rightDigimon.Digiclone.CloneLevel);
                    WriteShort(rightDigimon.Digiclone.ATValue);
                    WriteShort(rightDigimon.Digiclone.BLValue);
                    WriteShort(rightDigimon.Digiclone.CTValue);
                    WriteShort(0); //Valor atual do as (versao 487 nao tem)
                    WriteShort(rightDigimon.Digiclone.EVValue);
                    WriteShort(0); //Valor atual do HT (versao 487 nao tem)
                    WriteShort(rightDigimon.Digiclone.HPValue);

                    WriteShort(rightDigimon.Digiclone.ATLevel);
                    WriteShort(rightDigimon.Digiclone.BLLevel);
                    WriteShort(rightDigimon.Digiclone.CTLevel);
                    WriteShort(0); //Level atual do as (versao 487 nao tem)
                    WriteShort(rightDigimon.Digiclone.EVLevel);
                    WriteShort(0); //Level atual do HT (versao 487 nao tem)
                    WriteShort(rightDigimon.Digiclone.HPLevel);
                    break;

                case DigimonDataExchangeEnum.eDataChangeType_EvoSlot:
                    WriteByte((byte)leftDigimon.Evolutions.Count);
                    foreach (var evolution in leftDigimon.Evolutions)
                    {
                        WriteBytes(evolution.ToArray());
                    }
                    WriteByte((byte)rightDigimon.Evolutions.Count);
                    foreach(var evolution in rightDigimon.Evolutions)
                    {
                        WriteBytes(evolution.ToArray());
                    }
                    break;

                default:
                    break;
            }
        }
        public DigimonDataExchangePacket(DigimonDataExchangeEnum nDataChangeType, int result, byte leftDigimonSlot, byte rightDigimonSlot)
        {
            Type(PacketNumber);
            WriteInt((int)nDataChangeType);
            WriteByte(leftDigimonSlot);
            WriteByte(rightDigimonSlot);
            WriteInt(result);
        }
    }
}