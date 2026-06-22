namespace DigitalWorldOnline.Commons.Enums
{
    public enum DigimonDataExchangeEnum
    {
        eDataChangeType_None = -1,
        eDataChangeType_Begin = 0,
        eDataChangeType_Size = eDataChangeType_Begin,
        eDataChangeType_Inchant,
        eDataChangeType_EvoSlot,
        eDataChangeType_End = eDataChangeType_EvoSlot,
        eDataChangeType_Count,
    }
    public enum DigimonDataExchangeResultEnum
    {
        MESSAGE_LACK = 11040,	// (아이템 교환에 필요한) 아이템이 부족합니다.
        MESSAGE_MISMATCH_HATCHLV = 30799,	// 초월 디지몬의 데이터 교환은 같은 계열체의 초월 디지몬이 필요합니다
        MESSAGE_REGISTER = 30800,	// 모두 등록하세요
        MESSAGE_MISMATCH = 30801,	// 같은 계열체의 디지몬만 등록 할 수 있습니다.
        MESSAGE_PARTNERMON = 30802,	// 파트너몬은 등록 할수 없습니다.
        MESSAGE_COMPLETE = 30803,	// 완료되었습니다.
        MESSAGE_ACTION = 30804,	// 실행 하시겠습니까?
        MESSAGE_SAME = 30805,	// 동일하여 데이터를 교환 할 수 없습니다.

        NONE_SLOT = -1,
        NONE_MODEL = -1
    }
}
