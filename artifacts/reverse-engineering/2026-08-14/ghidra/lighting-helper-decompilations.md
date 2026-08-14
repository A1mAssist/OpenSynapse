## FUN_1800692e0 at `1800692e0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_1800692e0(undefined8 param_1,int param_2,uint *param_3,undefined4 *param_4,uint param_5,
                  undefined4 *param_6)

{
  undefined4 *puVar1;
  uint uVar2;
  short sVar3;
  short sVar4;
  short sVar5;
  short sVar6;
  short sVar7;
  short sVar8;
  short sVar9;
  short sVar10;
  short sVar11;
  short sVar12;
  short sVar13;
  char cVar14;
  char cVar18;
  char cVar22;
  char cVar26;
  undefined1 auVar30 [16];
  undefined8 uVar31;
  unkbyte10 Var32;
  undefined1 auVar33 [12];
  undefined1 auVar34 [14];
  short sVar35;
  undefined1 auVar36 [13];
  undefined1 auVar37 [13];
  undefined1 auVar38 [13];
  undefined1 auVar39 [13];
  double dVar40;
  double dVar41;
  uint uVar42;
  uint uVar43;
  ulonglong uVar44;
  uint uVar45;
  uint uVar46;
  uint uVar47;
  uint uVar48;
  uint uVar49;
  int iVar50;
  uint uVar51;
  ulonglong uVar52;
  float fVar55;
  float fVar56;
  undefined1 auVar53 [16];
  undefined1 auVar54 [16];
  float fVar57;
  float fVar58;
  float fVar59;
  float fVar60;
  float fVar61;
  float fVar62;
  short sVar63;
  short sVar64;
  undefined1 uVar68;
  int iVar65;
  int iVar69;
  int iVar70;
  int iVar71;
  short sVar72;
  short sVar73;
  char cVar76;
  char cVar77;
  char cVar78;
  char cVar15;
  char cVar16;
  char cVar17;
  char cVar19;
  char cVar20;
  char cVar21;
  char cVar23;
  char cVar24;
  char cVar25;
  char cVar27;
  char cVar28;
  char cVar29;
  undefined4 uVar66;
  undefined6 uVar67;
  undefined4 uVar74;
  undefined6 uVar75;
  
  dVar41 = DAT_1801aca10;
  dVar40 = DAT_1801aadd8;
  uVar45 = param_2 - 1;
  if (uVar45 == 0) {
    FUN_180194db0(param_6,0,(ulonglong)param_5 << 2);
    return;
  }
  uVar47 = *param_3;
  uVar44 = 0;
  do {
    if (uVar47 == 100) break;
    uVar46 = uVar47 * param_5;
    uVar47 = param_3[uVar44 + 1];
    uVar46 = uVar46 / 100;
    if ((uVar47 * param_5) / 100 <= uVar46) {
      uVar47 = (uint)(longlong)(((double)(uVar46 + 1) * dVar41) / (double)param_5 + dVar40);
      if (99 < uVar47) {
        uVar47 = 100;
      }
      param_3[uVar44 + 1] = uVar47;
    }
    uVar44 = uVar44 + 1;
  } while (uVar45 != uVar44);
  FUN_180194db0(param_6,0,(ulonglong)param_5 << 2);
  uVar42 = _UNK_1801ace88;
  uVar46 = _UNK_1801ace84;
  uVar47 = _DAT_1801ace80;
  uVar44 = 0;
  do {
    if (param_3[uVar44] == 100) {
      return;
    }
    uVar48 = param_3[uVar44] * param_5;
    uVar52 = uVar44 + 1;
    uVar43 = param_3[uVar44 + 1] * param_5;
    param_6[(ulonglong)uVar48 / 100] = param_4[uVar44];
    uVar49 = uVar48 / 100 + 1;
    if (uVar49 < uVar43 / 100) {
      uVar51 = uVar43 / 100 - 1;
      uVar2 = param_4[uVar52];
      param_6[uVar51] = uVar2;
      if (uVar48 / 100 < uVar51) {
        if (uVar49 < uVar51) {
          fVar58 = (float)(int)(uVar51 - uVar48 / 100);
          uVar66 = param_6[(ulonglong)uVar48 / 100];
          uVar68 = (undefined1)((uint)uVar66 >> 8);
          uVar44 = (ulonglong)CONCAT12(uVar68,(short)uVar66) & 0xffffffffffff00ff;
          auVar36._8_4_ = 0;
          auVar36._0_8_ = uVar44;
          auVar36[0xc] = (char)((uint)uVar66 >> 0x18);
          auVar37[8] = (char)((uint)uVar66 >> 0x10);
          auVar37._0_8_ = uVar44;
          auVar37[9] = 0;
          auVar37._10_3_ = auVar36._10_3_;
          auVar39._5_8_ = 0;
          auVar39._0_5_ = auVar37._8_5_;
          auVar38[4] = uVar68;
          auVar38._0_4_ = (uint)uVar44;
          auVar38[5] = 0;
          auVar38._6_7_ = SUB137(auVar39 << 0x40,6);
          uVar51 = (uint)uVar44 & 0xffff;
          uVar49 = (uint)(uint3)(auVar36._10_3_ >> 0x10);
          auVar53._0_4_ = (float)(int)((uVar2 & uVar47) - uVar51);
          auVar53._4_4_ = (float)(int)((uVar2 >> 8 & uVar46) - auVar38._4_4_);
          auVar53._8_4_ = (float)(int)((uVar2 >> 0x10 & uVar42) - auVar37._8_4_);
          auVar53._12_4_ = (float)(int)((uVar2 >> 0x18) - uVar49);
          auVar54._4_4_ = fVar58;
          auVar54._0_4_ = fVar58;
          auVar54._8_4_ = fVar58;
          auVar54._12_4_ = fVar58;
          auVar54 = divps(auVar53,auVar54);
          fVar59 = (float)uVar51;
          fVar60 = (float)auVar38._4_4_;
          fVar61 = (float)auVar37._8_4_;
          fVar62 = (float)uVar49;
          uVar44 = (ulonglong)(uVar48 / 100 + 1);
          uVar49 = (uVar43 / 100 ^ 2) - uVar48 / 100 & 3;
          fVar58 = auVar54._0_4_;
          fVar55 = auVar54._4_4_;
          fVar56 = auVar54._8_4_;
          fVar57 = auVar54._12_4_;
          if (uVar49 != 0) {
            iVar50 = uVar49 << 2;
            do {
              fVar59 = fVar59 + fVar58;
              fVar60 = fVar60 + fVar55;
              fVar61 = fVar61 + fVar56;
              fVar62 = fVar62 + fVar57;
              iVar65 = (int)fVar59;
              iVar69 = (int)fVar60;
              iVar70 = (int)fVar61;
              iVar71 = (int)fVar62;
              sVar3 = (short)iVar65;
              cVar14 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar65 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar65 >> 0x10);
              sVar63 = CONCAT11((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar65 >> 0x10) -
                                (0xff < sVar3),cVar14);
              sVar3 = (short)iVar69;
              cVar15 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar69 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar69 >> 0x10);
              uVar66 = CONCAT13((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar69 >> 0x10) -
                                (0xff < sVar3),CONCAT12(cVar15,sVar63));
              sVar3 = (short)iVar70;
              cVar16 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar70 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar70 >> 0x10);
              uVar67 = CONCAT15((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar70 >> 0x10) -
                                (0xff < sVar3),CONCAT14(cVar16,uVar66));
              sVar3 = (short)iVar71;
              cVar17 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar71 - (0xff < sVar3);
              sVar5 = (short)((uint)iVar71 >> 0x10);
              sVar3 = (short)((uint)uVar66 >> 0x10);
              sVar4 = (short)((uint6)uVar67 >> 0x20);
              sVar5 = (short)(CONCAT17((0 < sVar5) * (sVar5 < 0x100) * (char)((uint)iVar71 >> 0x10)
                                       - (0xff < sVar5),CONCAT16(cVar17,uVar67)) >> 0x30);
              param_6[uVar44] =
                   CONCAT13((0 < sVar5) * (sVar5 < 0x100) * cVar17 - (0xff < sVar5),
                            CONCAT12((0 < sVar4) * (sVar4 < 0x100) * cVar16 - (0xff < sVar4),
                                     CONCAT11((0 < sVar3) * (sVar3 < 0x100) * cVar15 -
                                              (0xff < sVar3),
                                              (0 < sVar63) * (sVar63 < 0x100) * cVar14 -
                                              (0xff < sVar63))));
              uVar44 = uVar44 + 1;
              iVar50 = iVar50 + -4;
            } while (iVar50 != 0);
          }
          if (2 < (uVar43 / 100 - uVar48 / 100) - 3) {
            do {
              iVar50 = (int)(fVar59 + fVar58);
              iVar65 = (int)(fVar60 + fVar55);
              iVar69 = (int)(fVar61 + fVar56);
              iVar70 = (int)(fVar62 + fVar57);
              sVar3 = (short)iVar50;
              cVar14 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar50 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar50 >> 0x10);
              sVar64 = CONCAT11((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar50 >> 0x10) -
                                (0xff < sVar3),cVar14);
              sVar3 = (short)iVar65;
              cVar15 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar65 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar65 >> 0x10);
              uVar66 = CONCAT13((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar65 >> 0x10) -
                                (0xff < sVar3),CONCAT12(cVar15,sVar64));
              sVar3 = (short)iVar69;
              cVar16 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar69 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar69 >> 0x10);
              uVar67 = CONCAT15((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar69 >> 0x10) -
                                (0xff < sVar3),CONCAT14(cVar16,uVar66));
              sVar3 = (short)iVar70;
              cVar17 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar70 - (0xff < sVar3);
              sVar63 = (short)((uint)iVar70 >> 0x10);
              fVar59 = fVar59 + fVar58 + fVar58;
              fVar60 = fVar60 + fVar55 + fVar55;
              fVar61 = fVar61 + fVar56 + fVar56;
              fVar62 = fVar62 + fVar57 + fVar57;
              iVar50 = (int)fVar59;
              iVar65 = (int)fVar60;
              iVar69 = (int)fVar61;
              iVar71 = (int)fVar62;
              sVar3 = (short)iVar50;
              cVar18 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar50 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar50 >> 0x10);
              sVar72 = CONCAT11((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar50 >> 0x10) -
                                (0xff < sVar3),cVar18);
              sVar3 = (short)iVar65;
              cVar19 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar65 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar65 >> 0x10);
              uVar74 = CONCAT13((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar65 >> 0x10) -
                                (0xff < sVar3),CONCAT12(cVar19,sVar72));
              sVar3 = (short)iVar69;
              cVar20 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar69 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar69 >> 0x10);
              uVar75 = CONCAT15((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar69 >> 0x10) -
                                (0xff < sVar3),CONCAT14(cVar20,uVar74));
              sVar3 = (short)iVar71;
              cVar21 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar71 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar71 >> 0x10);
              sVar4 = (short)((uint)uVar66 >> 0x10);
              sVar5 = (short)((uint6)uVar67 >> 0x20);
              sVar6 = (short)(CONCAT17((0 < sVar63) * (sVar63 < 0x100) *
                                       (char)((uint)iVar70 >> 0x10) - (0xff < sVar63),
                                       CONCAT16(cVar17,uVar67)) >> 0x30);
              sVar10 = (short)((uint)uVar74 >> 0x10);
              sVar11 = (short)((uint6)uVar75 >> 0x20);
              sVar13 = (short)(CONCAT17((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar71 >> 0x10)
                                        - (0xff < sVar3),CONCAT16(cVar21,uVar75)) >> 0x30);
              fVar59 = fVar59 + fVar58;
              fVar60 = fVar60 + fVar55;
              fVar61 = fVar61 + fVar56;
              fVar62 = fVar62 + fVar57;
              iVar50 = (int)fVar59;
              iVar65 = (int)fVar60;
              iVar69 = (int)fVar61;
              iVar71 = (int)fVar62;
              sVar3 = (short)iVar50;
              cVar22 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar50 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar50 >> 0x10);
              sVar73 = CONCAT11((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar50 >> 0x10) -
                                (0xff < sVar3),cVar22);
              sVar3 = (short)iVar65;
              cVar23 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar65 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar65 >> 0x10);
              uVar74 = CONCAT13((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar65 >> 0x10) -
                                (0xff < sVar3),CONCAT12(cVar23,sVar73));
              sVar3 = (short)iVar69;
              cVar24 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar69 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar69 >> 0x10);
              uVar75 = CONCAT15((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar69 >> 0x10) -
                                (0xff < sVar3),CONCAT14(cVar24,uVar74));
              sVar3 = (short)iVar71;
              cVar25 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar71 - (0xff < sVar3);
              sVar9 = (short)((uint)iVar71 >> 0x10);
              fVar59 = fVar59 + fVar58;
              fVar60 = fVar60 + fVar55;
              fVar61 = fVar61 + fVar56;
              fVar62 = fVar62 + fVar57;
              iVar50 = (int)fVar59;
              iVar65 = (int)fVar60;
              cVar76 = (char)((uint)iVar65 >> 0x10);
              iVar69 = (int)fVar61;
              cVar77 = (char)((uint)iVar69 >> 0x10);
              iVar70 = (int)fVar62;
              cVar78 = (char)((uint)iVar70 >> 0x10);
              uVar67 = CONCAT15((char)((uint)iVar65 >> 8),CONCAT14((char)iVar65,iVar50));
              uVar31 = CONCAT17((char)((uint)iVar65 >> 0x18),CONCAT16(cVar76,uVar67));
              Var32 = CONCAT19((char)((uint)iVar69 >> 8),CONCAT18((char)iVar69,uVar31));
              auVar33[10] = cVar77;
              auVar33._0_10_ = Var32;
              auVar33[0xb] = (char)((uint)iVar69 >> 0x18);
              auVar34[0xc] = (char)iVar70;
              auVar34._0_12_ = auVar33;
              auVar34[0xd] = (char)((uint)iVar70 >> 8);
              auVar30[0xe] = cVar78;
              auVar30._0_14_ = auVar34;
              auVar30[0xf] = (char)((uint)iVar70 >> 0x18);
              sVar3 = (short)iVar50;
              cVar26 = (0 < sVar3) * (sVar3 < 0x100) * (char)iVar50 - (0xff < sVar3);
              sVar3 = (short)((uint)iVar50 >> 0x10);
              sVar63 = (short)((uint6)uVar67 >> 0x20);
              cVar27 = (0 < sVar63) * (sVar63 < 0x100) * (char)iVar65 - (0xff < sVar63);
              sVar63 = (short)((ulonglong)uVar31 >> 0x30);
              sVar7 = (short)((unkuint10)Var32 >> 0x40);
              cVar28 = (0 < sVar7) * (sVar7 < 0x100) * (char)iVar69 - (0xff < sVar7);
              sVar7 = auVar33._10_2_;
              sVar8 = auVar34._12_2_;
              cVar29 = (0 < sVar8) * (sVar8 < 0x100) * (char)iVar70 - (0xff < sVar8);
              sVar8 = auVar30._14_2_;
              sVar35 = CONCAT11((0 < sVar3) * (sVar3 < 0x100) * (char)((uint)iVar50 >> 0x10) -
                                (0xff < sVar3),cVar26);
              uVar66 = CONCAT13((0 < sVar63) * (sVar63 < 0x100) * cVar76 - (0xff < sVar63),
                                CONCAT12(cVar27,sVar35));
              uVar67 = CONCAT15((0 < sVar7) * (sVar7 < 0x100) * cVar77 - (0xff < sVar7),
                                CONCAT14(cVar28,uVar66));
              sVar3 = (short)((uint)uVar74 >> 0x10);
              sVar63 = (short)((uint6)uVar75 >> 0x20);
              sVar7 = (short)(CONCAT17((0 < sVar9) * (sVar9 < 0x100) * (char)((uint)iVar71 >> 0x10)
                                       - (0xff < sVar9),CONCAT16(cVar25,uVar75)) >> 0x30);
              sVar9 = (short)((uint)uVar66 >> 0x10);
              sVar12 = (short)((uint6)uVar67 >> 0x20);
              sVar8 = (short)(CONCAT17((0 < sVar8) * (sVar8 < 0x100) * cVar78 - (0xff < sVar8),
                                       CONCAT16(cVar29,uVar67)) >> 0x30);
              puVar1 = param_6 + uVar44;
              *puVar1 = CONCAT13((0 < sVar6) * (sVar6 < 0x100) * cVar17 - (0xff < sVar6),
                                 CONCAT12((0 < sVar5) * (sVar5 < 0x100) * cVar16 - (0xff < sVar5),
                                          CONCAT11((0 < sVar4) * (sVar4 < 0x100) * cVar15 -
                                                   (0xff < sVar4),
                                                   (0 < sVar64) * (sVar64 < 0x100) * cVar14 -
                                                   (0xff < sVar64))));
              puVar1[1] = CONCAT13((0 < sVar13) * (sVar13 < 0x100) * cVar21 - (0xff < sVar13),
                                   CONCAT12((0 < sVar11) * (sVar11 < 0x100) * cVar20 -
                                            (0xff < sVar11),
                                            CONCAT11((0 < sVar10) * (sVar10 < 0x100) * cVar19 -
                                                     (0xff < sVar10),
                                                     (0 < sVar72) * (sVar72 < 0x100) * cVar18 -
                                                     (0xff < sVar72))));
              puVar1[2] = CONCAT13((0 < sVar7) * (sVar7 < 0x100) * cVar25 - (0xff < sVar7),
                                   CONCAT12((0 < sVar63) * (sVar63 < 0x100) * cVar24 -
                                            (0xff < sVar63),
                                            CONCAT11((0 < sVar3) * (sVar3 < 0x100) * cVar23 -
                                                     (0xff < sVar3),
                                                     (0 < sVar73) * (sVar73 < 0x100) * cVar22 -
                                                     (0xff < sVar73))));
              puVar1[3] = CONCAT13((0 < sVar8) * (sVar8 < 0x100) * cVar29 - (0xff < sVar8),
                                   CONCAT12((0 < sVar12) * (sVar12 < 0x100) * cVar28 -
                                            (0xff < sVar12),
                                            CONCAT11((0 < sVar9) * (sVar9 < 0x100) * cVar27 -
                                                     (0xff < sVar9),
                                                     (0 < sVar35) * (sVar35 < 0x100) * cVar26 -
                                                     (0xff < sVar35))));
              uVar44 = uVar44 + 4;
            } while (uVar43 / 100 - 1 != (int)uVar44);
          }
        }
        if (param_3[uVar52] == 100) {
          *param_6 = *param_4;
          param_6[param_5 - 1] = param_4[uVar52];
          return;
        }
      }
    }
    uVar44 = uVar52;
    if (uVar52 == uVar45) {
      return;
    }
  } while( true );
}


```

## FUN_180069590 at `180069590`

```c

void FUN_180069590(undefined8 param_1,undefined4 param_2,uint param_3,longlong param_4)

{
  undefined4 *puVar1;
  ulonglong uVar2;
  ulonglong uVar3;
  
  if (param_3 != 0 && param_4 != 0) {
    if (param_3 < 8) {
      uVar2 = 0;
    }
    else {
      uVar2 = (ulonglong)(param_3 & 0xfffffff8);
      uVar3 = 0;
      do {
        puVar1 = (undefined4 *)(param_4 + uVar3);
        *puVar1 = param_2;
        puVar1[1] = param_2;
        puVar1[2] = param_2;
        puVar1[3] = param_2;
        puVar1 = (undefined4 *)(param_4 + 0x10 + uVar3);
        *puVar1 = param_2;
        puVar1[1] = param_2;
        puVar1[2] = param_2;
        puVar1[3] = param_2;
        uVar3 = uVar3 + 0x20;
      } while (((ulonglong)param_3 * 4 & 0xffffffffffffffe0) != uVar3);
      if ((param_3 & 0xfffffff8) == param_3) {
        return;
      }
    }
    do {
      *(undefined4 *)(param_4 + uVar2 * 4) = param_2;
      uVar2 = uVar2 + 1;
    } while (param_3 != uVar2);
  }
  return;
}


```

## FUN_180065f50 at `180065f50`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8
FUN_180065f50(longlong param_1,int param_2,int param_3,int param_4,int param_5,int param_6,
             longlong param_7)

{
  int iVar1;
  int iVar2;
  int iVar3;
  int iVar4;
  int iVar5;
  int iVar6;
  int iVar7;
  uint uVar8;
  uint uVar9;
  float fVar10;
  float fVar11;
  
  uVar8 = FUN_180155ec0(*(undefined4 *)(param_1 + 0x1b0));
  uVar9 = FUN_18014de90(*(undefined4 *)(param_1 + 0x1b0));
  fVar11 = (float)(~-(uint)((float)(uVar8 & _DAT_1801aade0) < DAT_1801aca18) & uVar8);
  fVar10 = (float)(~-(uint)((float)(_DAT_1801aade0 & uVar9) < DAT_1801aca18) & uVar9);
  iVar1 = *(int *)(param_1 + 0x184) - param_2;
  iVar3 = iVar1;
  if ((*(int *)(param_1 + 0x18c) == 1) &&
     (((fVar11 <= 0.0 && (fVar10 <= 0.0)) || ((0.0 < fVar11 && (fVar10 < 0.0)))))) {
    iVar3 = iVar1 - *(int *)(param_1 + 0x1a4);
  }
  if ((*(byte *)(param_1 + 0x14) & 2) != 0) {
    iVar5 = iVar3;
    if (0.0 <= fVar10) {
      iVar5 = iVar1;
    }
    if (fVar11 < 0.0) {
      iVar5 = iVar3;
    }
    iVar3 = iVar5;
    if (fVar10 <= 0.0) {
      iVar3 = param_2;
    }
    if (0.0 < fVar11) {
      iVar3 = iVar5;
    }
    if (0.0 <= fVar10) {
      param_2 = iVar3;
    }
    if (fVar11 <= 0.0) {
      param_2 = iVar3;
    }
    if (fVar10 <= 0.0) {
      iVar1 = param_2;
    }
    iVar3 = iVar1;
    if (0.0 <= fVar11) {
      iVar3 = param_2;
    }
  }
  param_4 = param_4 - param_3;
  if ((0 < param_4) && (param_6 = param_6 - param_5, 0 < param_6)) {
    iVar6 = (int)((((float)*(int *)(param_1 + 0x1b8) * fVar10) / (float)param_6 +
                  ((float)*(int *)(param_1 + 0x1b4) * fVar11) / (float)param_4) *
                 (float)*(uint *)(param_1 + 0x1bc));
    iVar1 = *(int *)(param_1 + 0x1c);
    iVar5 = 0;
    do {
      iVar7 = 0;
      do {
        iVar4 = (int)((((float)iVar7 * fVar10) / (float)param_6 +
                      ((float)iVar5 * fVar11) / (float)param_4) * (float)*(uint *)(param_1 + 0x1bc))
        ;
        iVar2 = iVar4;
        if (((*(byte *)(param_1 + 0x14) & 2) != 0) && (iVar2 = iVar4 - iVar6, iVar4 < iVar6)) {
          iVar2 = iVar6 - iVar4;
        }
        *(undefined4 *)
         (param_7 +
         (ulonglong)(uint)(*(int *)(param_1 + 0x1c) * iVar5 + param_5 + param_3 * iVar1 + iVar7) * 4
         ) = *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              ((ulonglong)(iVar2 + iVar3 + *(uint *)(param_1 + 0x184) * 2) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        iVar7 = iVar7 + 1;
      } while (param_6 != iVar7);
      iVar5 = iVar5 + 1;
    } while (iVar5 != param_4);
  }
  return 0;
}


```

## FUN_180066160 at `180066160`

```c

undefined8 FUN_180066160(longlong param_1,int param_2,int param_3,longlong param_4)

{
  uint uVar1;
  longlong lVar2;
  int iVar3;
  uint uVar4;
  uint uVar5;
  uint uVar6;
  uint uVar7;
  ulonglong uVar8;
  uint uVar9;
  uint uVar10;
  int iVar11;
  uint uVar12;
  int iVar13;
  ulonglong uVar14;
  
  uVar10 = *(uint *)(param_1 + 0xa0) & 0xffff;
  uVar12 = *(uint *)(param_1 + 0xa0) >> 0x10;
  uVar7 = *(uint *)(param_1 + 0xa4) & 0xffff;
  uVar8 = (ulonglong)uVar7;
  uVar9 = *(uint *)(param_1 + 0xa4) >> 0x10;
  uVar14 = (ulonglong)uVar9;
  uVar1 = 0;
  if (param_3 == 1) {
    if (uVar12 <= uVar10) {
      uVar1 = (uVar10 - uVar12) + 1;
      uVar4 = 0;
      do {
        *(undefined4 *)
         (param_4 + (ulonglong)((uVar12 + uVar4) * *(int *)(param_1 + 0x1c) + uVar7) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)(int)uVar4 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar4 = uVar4 + 1;
      } while (uVar1 != uVar4);
    }
    uVar4 = uVar1;
    if (uVar9 < uVar7) {
      iVar3 = uVar1 + uVar7;
      uVar5 = uVar7;
      do {
        uVar5 = uVar5 - 1;
        *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar10 + uVar5) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)uVar1 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar1 = uVar1 + 1;
        uVar4 = iVar3 - uVar9;
      } while (uVar5 != uVar9);
    }
    uVar1 = uVar4;
    if (uVar12 < uVar10) {
      uVar1 = (uVar4 + uVar10) - uVar12;
      uVar5 = uVar10;
      do {
        uVar5 = uVar5 - 1;
        *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar5 + uVar9) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)uVar4 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar4 = uVar4 + 1;
      } while (uVar5 != uVar12);
    }
    uVar4 = uVar9 + 1;
    if (uVar4 < uVar7) {
      do {
        *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar12 + uVar4) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)uVar1 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar1 = uVar1 + 1;
        uVar4 = uVar4 + 1;
      } while (uVar7 != uVar4);
    }
  }
  else {
    if (uVar12 <= uVar10) {
      uVar1 = (uVar10 - uVar12) + 1;
      uVar4 = 0;
      do {
        *(undefined4 *)
         (param_4 + (ulonglong)((uVar12 + uVar4) * *(int *)(param_1 + 0x1c) + uVar9) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)(int)uVar4 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar4 = uVar4 + 1;
      } while (uVar1 != uVar4);
    }
    uVar4 = uVar1;
    if (uVar9 < uVar7) {
      uVar4 = (uVar1 + uVar7) - uVar9;
      uVar5 = uVar9;
      do {
        uVar5 = uVar5 + 1;
        *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar10 + uVar5) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)uVar1 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar1 = uVar1 + 1;
      } while (uVar5 != uVar7);
    }
    uVar5 = uVar4;
    uVar1 = uVar7;
    if (uVar12 < uVar10) {
      uVar5 = (uVar4 + uVar10) - uVar12;
      uVar6 = uVar10;
      do {
        uVar6 = uVar6 - 1;
        *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar6 + uVar7) * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) +
              (((longlong)
                ((float)uVar4 / *(float *)(param_1 + 400) +
                (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
              (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
        uVar4 = uVar4 + 1;
      } while (uVar6 != uVar12);
    }
    while (uVar1 = uVar1 - 1, (int)uVar9 < (int)uVar1) {
      *(undefined4 *)(param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar12 + uVar1) * 4) =
           *(undefined4 *)
            (*(longlong *)(param_1 + 0x198) +
            (((longlong)
              ((float)uVar5 / *(float *)(param_1 + 400) +
              (float)(*(uint *)(param_1 + 0x184) - param_2)) & 0xffffffffU) %
            (ulonglong)*(uint *)(param_1 + 0x184)) * 4);
      uVar5 = uVar5 + 1;
    }
  }
  iVar3 = uVar10 - 1;
  if ((int)uVar12 < iVar3) {
    iVar11 = uVar12 + 1;
    lVar2 = (longlong)(int)(~uVar9 + uVar7) << 2;
    do {
      uVar8 = uVar8 - 1;
      uVar14 = uVar14 + 1;
      if ((longlong)uVar8 <= (longlong)uVar14) break;
      iVar13 = (int)uVar14;
      FUN_180194710(param_4 + (ulonglong)(uint)(*(int *)(param_1 + 0x1c) * iVar11 + iVar13) * 4,
                    param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar12 + iVar13) * 4,lVar2);
      FUN_180194710(param_4 + (ulonglong)(uint)(*(int *)(param_1 + 0x1c) * iVar3 + iVar13) * 4,
                    param_4 + (ulonglong)(*(int *)(param_1 + 0x1c) * uVar10 + iVar13) * 4,lVar2);
      iVar11 = iVar11 + 1;
      lVar2 = lVar2 + -8;
      iVar3 = iVar3 + -1;
    } while (iVar11 <= iVar3);
  }
  if ((int)uVar9 < (int)(uVar7 - 1)) {
    uVar1 = uVar10 - 1;
    iVar3 = 1;
    do {
      if ((int)(uVar10 - iVar3) <= (int)(iVar3 + uVar12)) {
        return 0;
      }
      uVar4 = uVar12;
      do {
        iVar11 = *(int *)(param_1 + 0x1c) * (iVar3 + uVar4);
        *(undefined4 *)(param_4 + (ulonglong)(iVar11 + iVar3 + uVar9) * 4) =
             *(undefined4 *)(param_4 + (ulonglong)(iVar11 + uVar9) * 4);
        uVar5 = (iVar3 + uVar4) * *(int *)(param_1 + 0x1c) + uVar7;
        *(undefined4 *)(param_4 + (ulonglong)(uVar5 - iVar3) * 4) =
             *(undefined4 *)(param_4 + (ulonglong)uVar5 * 4);
        uVar4 = uVar4 + 1;
      } while (uVar1 != uVar4);
      iVar11 = iVar3 + uVar9;
      iVar3 = iVar3 + 1;
      uVar1 = uVar1 - 2;
    } while (iVar11 + 1 <= (int)(uVar7 - iVar3));
  }
  return 0;
}


```

## FUN_180069600 at `180069600`

```c

char * FUN_180069600(longlong param_1,char *param_2)

{
  undefined1 auVar1 [16];
  undefined1 auVar2 [16];
  undefined1 auVar3 [16];
  uint uVar4;
  longlong lVar5;
  char cVar6;
  char cVar7;
  ulonglong uVar8;
  char cVar9;
  ulonglong uVar10;
  ulonglong uVar11;
  ulonglong uVar12;
  
  uVar10 = *(ulonglong *)(param_1 + 0x178);
  do {
    lVar5 = uVar10 * 0x343fd;
    uVar10 = lVar5 + 0x100269ec2;
    if (-1 < (longlong)(lVar5 + 0x269ec3U)) {
      uVar10 = lVar5 + 0x269ec3U;
    }
    uVar10 = (lVar5 - (uVar10 & 0xffffffff00000000)) + 0x269ec3;
    uVar8 = (longlong)uVar10 >> 0x10 ^ uVar10;
    auVar1 = SEXT816((longlong)uVar8) * ZEXT816(0x8080808080808081);
    lVar5 = uVar10 * 0x343fd;
    uVar10 = lVar5 + 0x100269ec2;
    if (-1 < (longlong)(lVar5 + 0x269ec3U)) {
      uVar10 = lVar5 + 0x269ec3U;
    }
    uVar10 = (lVar5 - (uVar10 & 0xffffffff00000000)) + 0x269ec3;
    uVar11 = (longlong)uVar10 >> 0x10 ^ uVar10;
    auVar2 = SEXT816((longlong)uVar11) * ZEXT816(0x8080808080808081);
    lVar5 = uVar10 * 0x343fd;
    uVar10 = lVar5 + 0x100269ec2;
    if (-1 < (longlong)(lVar5 + 0x269ec3U)) {
      uVar10 = lVar5 + 0x269ec3U;
    }
    uVar10 = (lVar5 - (uVar10 & 0xffffffff00000000)) + 0x269ec3;
    uVar12 = (longlong)uVar10 >> 0x10 ^ uVar10;
    auVar3 = SEXT816((longlong)uVar12) * ZEXT816(0x8080808080808081);
    uVar4 = (int)uVar8 + ((auVar1._8_4_ >> 7) - (auVar1._12_4_ >> 0x1f)) & 0xff;
    cVar6 = (0x55 < uVar4) << 7;
    if (0xaa < uVar4) {
      cVar6 = -1;
    }
    uVar4 = (int)uVar11 + ((auVar2._8_4_ >> 7) - (auVar2._12_4_ >> 0x1f)) & 0xff;
    cVar7 = (0x55 < uVar4) << 7;
    if (0xaa < uVar4) {
      cVar7 = -1;
    }
    uVar4 = (int)uVar12 + ((auVar3._8_4_ >> 7) - (auVar3._12_4_ >> 0x1f)) & 0xff;
    cVar9 = (0x55 < uVar4) << 7;
    if (0xaa < uVar4) {
      cVar9 = -1;
    }
  } while ((cVar6 == cVar7) && (cVar7 == cVar9));
  *(ulonglong *)(param_1 + 0x178) = uVar10;
  *param_2 = cVar6;
  param_2[1] = cVar7;
  param_2[2] = cVar9;
  param_2[3] = '\0';
  return param_2;
}


```

## FUN_180069140 at `180069140`

```c

undefined8 * FUN_180069140(undefined8 *param_1)

{
  *param_1 = CEffect::vftable;
  param_1[1] = 0;
  *(undefined2 *)(param_1 + 2) = 0;
  param_1[0x2f] = 0;
  FUN_180194db0((longlong)param_1 + 0x14,0,0x161);
  return param_1;
}


```

## FUN_180069190 at `180069190`

```c

void FUN_180069190(void)

{
  return;
}


```

## FUN_18004b6c0 at `18004b6c0`

```c

int FUN_18004b6c0(longlong param_1,uint param_2)

{
  uint uVar1;
  int iVar2;
  int iVar3;
  undefined1 auStack_198 [32];
  undefined4 *local_178;
  undefined8 *local_170;
  undefined8 local_160;
  undefined4 local_154;
  undefined1 local_150 [312];
  ulonglong local_18;
  
  local_18 = DAT_1801f4b40 ^ (ulonglong)auStack_198;
  FUN_180045010("EffectEngine",param_1,"Init\n");
  uVar1 = 0x19;
  if (param_2 < 0x19) {
    uVar1 = param_2;
  }
  *(uint *)(param_1 + 0xd0) = uVar1;
  *(undefined8 *)(param_1 + 0xd4) = 0;
  *(undefined8 *)(param_1 + 0xdc) = 0;
  iVar2 = FUN_180041a20(param_1 + 8,0xffffffff);
  if (iVar2 < 0) {
    if ((local_18 ^ (ulonglong)auStack_198) != DAT_1801f4b40) goto LAB_18004b7b5;
  }
  else {
    iVar2 = 0;
    FUN_180194db0(local_150,0,0x135);
    local_154 = 0x80001;
    local_170 = &local_160;
    local_178 = &local_154;
    iVar3 = FUN_180051200(param_1,*(undefined4 *)(param_1 + 0xd4),*(undefined4 *)(param_1 + 0xd8),
                          *(undefined4 *)(param_1 + 0xd0));
    if (-1 < iVar3) {
      *(undefined8 *)(param_1 + 0xb0) = local_160;
    }
    if ((local_18 ^ (ulonglong)auStack_198) != DAT_1801f4b40) {
LAB_18004b7b5:
                    /* WARNING: Subroutine does not return */
      FUN_1800b9f70();
    }
  }
  return iVar2;
}


```

## FUN_1801551d0 at `1801551d0`

```c

uint FUN_1801551d0(void)

{
  longlong lVar1;
  uint uVar2;
  
  lVar1 = FUN_18016b57c();
  uVar2 = *(int *)(lVar1 + 0x28) * 0x343fd + 0x269ec3;
  *(uint *)(lVar1 + 0x28) = uVar2;
  return uVar2 >> 0x10 & 0x7fff;
}


```

## FUN_180194710 at `180194710`

```c

void FUN_180194710(undefined8 *param_1,undefined8 *param_2,ulonglong param_3)

{
  undefined8 *puVar1;
  undefined8 *puVar2;
  undefined1 auVar3 [32];
  undefined1 auVar4 [32];
  undefined1 auVar5 [32];
  undefined1 auVar6 [32];
  undefined1 uVar7;
  undefined2 uVar8;
  undefined4 uVar9;
  undefined8 uVar10;
  undefined8 uVar11;
  undefined8 uVar12;
  undefined8 uVar13;
  undefined8 uVar14;
  undefined8 uVar15;
  undefined8 uVar16;
  undefined8 uVar17;
  undefined8 uVar18;
  undefined8 uVar19;
  undefined8 uVar20;
  undefined8 uVar21;
  undefined8 uVar22;
  undefined1 (*pauVar23) [32];
  undefined1 (*pauVar24) [32];
  undefined8 *puVar25;
  undefined8 *puVar26;
  undefined1 (*pauVar27) [32];
  undefined1 (*pauVar28) [32];
  ulonglong uVar29;
  longlong lVar30;
  ulonglong uVar31;
  undefined8 uVar32;
  undefined8 uVar33;
  
  switch(param_3) {
  case 0:
    return;
  case 1:
    *(undefined1 *)param_1 = *(undefined1 *)param_2;
    return;
  case 2:
    *(undefined2 *)param_1 = *(undefined2 *)param_2;
    return;
  case 3:
    uVar7 = *(undefined1 *)((longlong)param_2 + 2);
    *(undefined2 *)param_1 = *(undefined2 *)param_2;
    *(undefined1 *)((longlong)param_1 + 2) = uVar7;
    return;
  case 4:
    *(undefined4 *)param_1 = *(undefined4 *)param_2;
    return;
  case 5:
    uVar7 = *(undefined1 *)((longlong)param_2 + 4);
    *(undefined4 *)param_1 = *(undefined4 *)param_2;
    *(undefined1 *)((longlong)param_1 + 4) = uVar7;
    return;
  case 6:
    uVar8 = *(undefined2 *)((longlong)param_2 + 4);
    *(undefined4 *)param_1 = *(undefined4 *)param_2;
    *(undefined2 *)((longlong)param_1 + 4) = uVar8;
    return;
  case 7:
    uVar8 = *(undefined2 *)((longlong)param_2 + 4);
    uVar7 = *(undefined1 *)((longlong)param_2 + 6);
    *(undefined4 *)param_1 = *(undefined4 *)param_2;
    *(undefined2 *)((longlong)param_1 + 4) = uVar8;
    *(undefined1 *)((longlong)param_1 + 6) = uVar7;
    return;
  case 8:
    *param_1 = *param_2;
    return;
  case 9:
    uVar7 = *(undefined1 *)(param_2 + 1);
    *param_1 = *param_2;
    *(undefined1 *)(param_1 + 1) = uVar7;
    return;
  case 10:
    uVar8 = *(undefined2 *)(param_2 + 1);
    *param_1 = *param_2;
    *(undefined2 *)(param_1 + 1) = uVar8;
    return;
  case 0xb:
    uVar8 = *(undefined2 *)(param_2 + 1);
    uVar7 = *(undefined1 *)((longlong)param_2 + 10);
    *param_1 = *param_2;
    *(undefined2 *)(param_1 + 1) = uVar8;
    *(undefined1 *)((longlong)param_1 + 10) = uVar7;
    return;
  case 0xc:
    uVar9 = *(undefined4 *)(param_2 + 1);
    *param_1 = *param_2;
    *(undefined4 *)(param_1 + 1) = uVar9;
    return;
  case 0xd:
    uVar9 = *(undefined4 *)(param_2 + 1);
    uVar7 = *(undefined1 *)((longlong)param_2 + 0xc);
    *param_1 = *param_2;
    *(undefined4 *)(param_1 + 1) = uVar9;
    *(undefined1 *)((longlong)param_1 + 0xc) = uVar7;
    return;
  case 0xe:
    uVar9 = *(undefined4 *)(param_2 + 1);
    uVar8 = *(undefined2 *)((longlong)param_2 + 0xc);
    *param_1 = *param_2;
    *(undefined4 *)(param_1 + 1) = uVar9;
    *(undefined2 *)((longlong)param_1 + 0xc) = uVar8;
    return;
  case 0xf:
    uVar9 = *(undefined4 *)(param_2 + 1);
    uVar8 = *(undefined2 *)((longlong)param_2 + 0xc);
    uVar7 = *(undefined1 *)((longlong)param_2 + 0xe);
    *param_1 = *param_2;
    *(undefined4 *)(param_1 + 1) = uVar9;
    *(undefined2 *)((longlong)param_1 + 0xc) = uVar8;
    *(undefined1 *)((longlong)param_1 + 0xe) = uVar7;
    return;
  }
  if (param_3 < 0x21) {
    uVar10 = param_2[1];
    puVar26 = (undefined8 *)((longlong)param_2 + (param_3 - 0x10));
    uVar11 = *puVar26;
    uVar12 = puVar26[1];
    *param_1 = *param_2;
    param_1[1] = uVar10;
    param_1 = (undefined8 *)((longlong)param_1 + (param_3 - 0x10));
    *param_1 = uVar11;
    param_1[1] = uVar12;
    return;
  }
  puVar26 = (undefined8 *)((longlong)param_2 + param_3);
  if (param_1 <= param_2) {
    puVar26 = param_1;
  }
  if (puVar26 <= param_1) {
    if (DAT_1801f4e80 < 3) {
      if ((param_3 < 0x801) || (((byte)DAT_1801f8864 & 2) == 0)) {
        if (0x80 < param_3) {
          lVar30 = ((ulonglong)param_1 & 0xf) - 0x10;
          param_1 = (undefined8 *)((longlong)param_1 - lVar30);
          param_2 = (undefined8 *)((longlong)param_2 - lVar30);
          param_3 = param_3 + lVar30;
          if (0x80 < param_3) {
            do {
              uVar10 = param_2[1];
              uVar11 = param_2[2];
              uVar12 = param_2[3];
              uVar13 = param_2[4];
              uVar32 = param_2[5];
              uVar33 = param_2[6];
              uVar14 = param_2[7];
              *param_1 = *param_2;
              param_1[1] = uVar10;
              param_1[2] = uVar11;
              param_1[3] = uVar12;
              param_1[4] = uVar13;
              param_1[5] = uVar32;
              param_1[6] = uVar33;
              param_1[7] = uVar14;
              uVar10 = param_2[9];
              uVar11 = param_2[10];
              uVar12 = param_2[0xb];
              uVar13 = param_2[0xc];
              uVar32 = param_2[0xd];
              uVar33 = param_2[0xe];
              uVar14 = param_2[0xf];
              param_1[8] = param_2[8];
              param_1[9] = uVar10;
              param_1[10] = uVar11;
              param_1[0xb] = uVar12;
              param_1[0xc] = uVar13;
              param_1[0xd] = uVar32;
              param_1[0xe] = uVar33;
              param_1[0xf] = uVar14;
              param_1 = param_1 + 0x10;
              param_2 = param_2 + 0x10;
              param_3 = param_3 - 0x80;
            } while (0x7f < param_3);
          }
        }
                    /* WARNING: Could not recover jumptable at 0x000180194c26. Too many branches */
                    /* WARNING: Treating indirect jump as call */
        (*(code *)((ulonglong)*(uint *)(&DAT_1801c33a8 + (param_3 + 0xf >> 4) * 4) + 0x180000000))()
        ;
        return;
      }
    }
    else if (((param_3 < 0x2001) || (0x180000 < param_3)) || (((byte)DAT_1801f8864 & 2) == 0)) {
      uVar10 = *param_2;
      uVar11 = param_2[1];
      uVar12 = param_2[2];
      uVar13 = param_2[3];
      puVar26 = (undefined8 *)((longlong)param_2 + (param_3 - 0x20));
      uVar32 = *puVar26;
      uVar33 = puVar26[1];
      uVar14 = puVar26[2];
      uVar15 = puVar26[3];
      if (0x100 < param_3) {
        lVar30 = ((ulonglong)param_1 & 0x1f) - 0x20;
        pauVar23 = (undefined1 (*) [32])((longlong)param_1 - lVar30);
        pauVar27 = (undefined1 (*) [32])((longlong)param_2 - lVar30);
        param_3 = param_3 + lVar30;
        if (0x100 < param_3) {
          if (0x180000 < param_3) {
            do {
              uVar29 = param_3;
              pauVar28 = pauVar27;
              pauVar24 = pauVar23;
              auVar3 = pauVar28[1];
              auVar4 = pauVar28[2];
              auVar5 = pauVar28[3];
              auVar6 = vmovntdq_avx(*pauVar28);
              *pauVar24 = auVar6;
              auVar3 = vmovntdq_avx(auVar3);
              pauVar24[1] = auVar3;
              auVar3 = vmovntdq_avx(auVar4);
              pauVar24[2] = auVar3;
              auVar3 = vmovntdq_avx(auVar5);
              pauVar24[3] = auVar3;
              auVar3 = pauVar28[5];
              auVar4 = pauVar28[6];
              auVar5 = pauVar28[7];
              auVar6 = vmovntdq_avx(pauVar28[4]);
              pauVar24[4] = auVar6;
              auVar3 = vmovntdq_avx(auVar3);
              pauVar24[5] = auVar3;
              auVar3 = vmovntdq_avx(auVar4);
              pauVar24[6] = auVar3;
              auVar3 = vmovntdq_avx(auVar5);
              pauVar24[7] = auVar3;
              pauVar23 = pauVar24 + 8;
              pauVar27 = pauVar28 + 8;
              param_3 = uVar29 - 0x100;
            } while (0xff < uVar29 - 0x100);
            uVar31 = uVar29 - 0xe1 & 0xffffffffffffffe0;
            switch(uVar29) {
            case 0x1e1:
            case 0x1e2:
            case 0x1e3:
            case 0x1e4:
            case 0x1e5:
            case 0x1e6:
            case 0x1e7:
            case 0x1e8:
            case 0x1e9:
            case 0x1ea:
            case 0x1eb:
            case 0x1ec:
            case 0x1ed:
            case 0x1ee:
            case 0x1ef:
            case 0x1f0:
            case 0x1f1:
            case 0x1f2:
            case 499:
            case 500:
            case 0x1f5:
            case 0x1f6:
            case 0x1f7:
            case 0x1f8:
            case 0x1f9:
            case 0x1fa:
            case 0x1fb:
            case 0x1fc:
            case 0x1fd:
            case 0x1fe:
            case 0x1ff:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(*pauVar28 + uVar31));
              *(undefined1 (*) [32])(*pauVar24 + uVar31) = auVar3;
            case 0x1c1:
            case 0x1c2:
            case 0x1c3:
            case 0x1c4:
            case 0x1c5:
            case 0x1c6:
            case 0x1c7:
            case 0x1c8:
            case 0x1c9:
            case 0x1ca:
            case 0x1cb:
            case 0x1cc:
            case 0x1cd:
            case 0x1ce:
            case 0x1cf:
            case 0x1d0:
            case 0x1d1:
            case 0x1d2:
            case 0x1d3:
            case 0x1d4:
            case 0x1d5:
            case 0x1d6:
            case 0x1d7:
            case 0x1d8:
            case 0x1d9:
            case 0x1da:
            case 0x1db:
            case 0x1dc:
            case 0x1dd:
            case 0x1de:
            case 0x1df:
            case 0x1e0:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[1] + uVar31));
              *(undefined1 (*) [32])(pauVar24[1] + uVar31) = auVar3;
            case 0x1a1:
            case 0x1a2:
            case 0x1a3:
            case 0x1a4:
            case 0x1a5:
            case 0x1a6:
            case 0x1a7:
            case 0x1a8:
            case 0x1a9:
            case 0x1aa:
            case 0x1ab:
            case 0x1ac:
            case 0x1ad:
            case 0x1ae:
            case 0x1af:
            case 0x1b0:
            case 0x1b1:
            case 0x1b2:
            case 0x1b3:
            case 0x1b4:
            case 0x1b5:
            case 0x1b6:
            case 0x1b7:
            case 0x1b8:
            case 0x1b9:
            case 0x1ba:
            case 0x1bb:
            case 0x1bc:
            case 0x1bd:
            case 0x1be:
            case 0x1bf:
            case 0x1c0:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[2] + uVar31));
              *(undefined1 (*) [32])(pauVar24[2] + uVar31) = auVar3;
            case 0x181:
            case 0x182:
            case 0x183:
            case 0x184:
            case 0x185:
            case 0x186:
            case 0x187:
            case 0x188:
            case 0x189:
            case 0x18a:
            case 0x18b:
            case 0x18c:
            case 0x18d:
            case 0x18e:
            case 399:
            case 400:
            case 0x191:
            case 0x192:
            case 0x193:
            case 0x194:
            case 0x195:
            case 0x196:
            case 0x197:
            case 0x198:
            case 0x199:
            case 0x19a:
            case 0x19b:
            case 0x19c:
            case 0x19d:
            case 0x19e:
            case 0x19f:
            case 0x1a0:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[3] + uVar31));
              *(undefined1 (*) [32])(pauVar24[3] + uVar31) = auVar3;
            case 0x161:
            case 0x162:
            case 0x163:
            case 0x164:
            case 0x165:
            case 0x166:
            case 0x167:
            case 0x168:
            case 0x169:
            case 0x16a:
            case 0x16b:
            case 0x16c:
            case 0x16d:
            case 0x16e:
            case 0x16f:
            case 0x170:
            case 0x171:
            case 0x172:
            case 0x173:
            case 0x174:
            case 0x175:
            case 0x176:
            case 0x177:
            case 0x178:
            case 0x179:
            case 0x17a:
            case 0x17b:
            case 0x17c:
            case 0x17d:
            case 0x17e:
            case 0x17f:
            case 0x180:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[4] + uVar31));
              *(undefined1 (*) [32])(pauVar24[4] + uVar31) = auVar3;
            case 0x141:
            case 0x142:
            case 0x143:
            case 0x144:
            case 0x145:
            case 0x146:
            case 0x147:
            case 0x148:
            case 0x149:
            case 0x14a:
            case 0x14b:
            case 0x14c:
            case 0x14d:
            case 0x14e:
            case 0x14f:
            case 0x150:
            case 0x151:
            case 0x152:
            case 0x153:
            case 0x154:
            case 0x155:
            case 0x156:
            case 0x157:
            case 0x158:
            case 0x159:
            case 0x15a:
            case 0x15b:
            case 0x15c:
            case 0x15d:
            case 0x15e:
            case 0x15f:
            case 0x160:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[5] + uVar31));
              *(undefined1 (*) [32])(pauVar24[5] + uVar31) = auVar3;
            case 0x121:
            case 0x122:
            case 0x123:
            case 0x124:
            case 0x125:
            case 0x126:
            case 0x127:
            case 0x128:
            case 0x129:
            case 0x12a:
            case 299:
            case 300:
            case 0x12d:
            case 0x12e:
            case 0x12f:
            case 0x130:
            case 0x131:
            case 0x132:
            case 0x133:
            case 0x134:
            case 0x135:
            case 0x136:
            case 0x137:
            case 0x138:
            case 0x139:
            case 0x13a:
            case 0x13b:
            case 0x13c:
            case 0x13d:
            case 0x13e:
            case 0x13f:
            case 0x140:
              auVar3 = vmovntdq_avx(*(undefined1 (*) [32])(pauVar28[6] + uVar31));
              *(undefined1 (*) [32])(pauVar24[6] + uVar31) = auVar3;
            default:
              puVar26 = (undefined8 *)(pauVar24[-1] + uVar29);
              *puVar26 = uVar32;
              puVar26[1] = uVar33;
              puVar26[2] = uVar14;
              puVar26[3] = uVar15;
            case 0x100:
              *param_1 = uVar10;
              param_1[1] = uVar11;
              param_1[2] = uVar12;
              param_1[3] = uVar13;
              return;
            }
          }
          do {
            uVar10 = *(undefined8 *)(*pauVar27 + 8);
            uVar11 = *(undefined8 *)(*pauVar27 + 0x10);
            uVar12 = *(undefined8 *)(*pauVar27 + 0x18);
            uVar13 = *(undefined8 *)pauVar27[1];
            uVar32 = *(undefined8 *)(pauVar27[1] + 8);
            uVar33 = *(undefined8 *)(pauVar27[1] + 0x10);
            uVar14 = *(undefined8 *)(pauVar27[1] + 0x18);
            uVar15 = *(undefined8 *)pauVar27[2];
            uVar16 = *(undefined8 *)(pauVar27[2] + 8);
            uVar17 = *(undefined8 *)(pauVar27[2] + 0x10);
            uVar18 = *(undefined8 *)(pauVar27[2] + 0x18);
            uVar19 = *(undefined8 *)pauVar27[3];
            uVar20 = *(undefined8 *)(pauVar27[3] + 8);
            uVar21 = *(undefined8 *)(pauVar27[3] + 0x10);
            uVar22 = *(undefined8 *)(pauVar27[3] + 0x18);
            *(undefined8 *)*pauVar23 = *(undefined8 *)*pauVar27;
            *(undefined8 *)(*pauVar23 + 8) = uVar10;
            *(undefined8 *)(*pauVar23 + 0x10) = uVar11;
            *(undefined8 *)(*pauVar23 + 0x18) = uVar12;
            *(undefined8 *)pauVar23[1] = uVar13;
            *(undefined8 *)(pauVar23[1] + 8) = uVar32;
            *(undefined8 *)(pauVar23[1] + 0x10) = uVar33;
            *(undefined8 *)(pauVar23[1] + 0x18) = uVar14;
            *(undefined8 *)pauVar23[2] = uVar15;
            *(undefined8 *)(pauVar23[2] + 8) = uVar16;
            *(undefined8 *)(pauVar23[2] + 0x10) = uVar17;
            *(undefined8 *)(pauVar23[2] + 0x18) = uVar18;
            *(undefined8 *)pauVar23[3] = uVar19;
            *(undefined8 *)(pauVar23[3] + 8) = uVar20;
            *(undefined8 *)(pauVar23[3] + 0x10) = uVar21;
            *(undefined8 *)(pauVar23[3] + 0x18) = uVar22;
            uVar10 = *(undefined8 *)(pauVar27[4] + 8);
            uVar11 = *(undefined8 *)(pauVar27[4] + 0x10);
            uVar12 = *(undefined8 *)(pauVar27[4] + 0x18);
            uVar13 = *(undefined8 *)pauVar27[5];
            uVar32 = *(undefined8 *)(pauVar27[5] + 8);
            uVar33 = *(undefined8 *)(pauVar27[5] + 0x10);
            uVar14 = *(undefined8 *)(pauVar27[5] + 0x18);
            uVar15 = *(undefined8 *)pauVar27[6];
            uVar16 = *(undefined8 *)(pauVar27[6] + 8);
            uVar17 = *(undefined8 *)(pauVar27[6] + 0x10);
            uVar18 = *(undefined8 *)(pauVar27[6] + 0x18);
            uVar19 = *(undefined8 *)pauVar27[7];
            uVar20 = *(undefined8 *)(pauVar27[7] + 8);
            uVar21 = *(undefined8 *)(pauVar27[7] + 0x10);
            uVar22 = *(undefined8 *)(pauVar27[7] + 0x18);
            *(undefined8 *)pauVar23[4] = *(undefined8 *)pauVar27[4];
            *(undefined8 *)(pauVar23[4] + 8) = uVar10;
            *(undefined8 *)(pauVar23[4] + 0x10) = uVar11;
            *(undefined8 *)(pauVar23[4] + 0x18) = uVar12;
            *(undefined8 *)pauVar23[5] = uVar13;
            *(undefined8 *)(pauVar23[5] + 8) = uVar32;
            *(undefined8 *)(pauVar23[5] + 0x10) = uVar33;
            *(undefined8 *)(pauVar23[5] + 0x18) = uVar14;
            *(undefined8 *)pauVar23[6] = uVar15;
            *(undefined8 *)(pauVar23[6] + 8) = uVar16;
            *(undefined8 *)(pauVar23[6] + 0x10) = uVar17;
            *(undefined8 *)(pauVar23[6] + 0x18) = uVar18;
            *(undefined8 *)pauVar23[7] = uVar19;
            *(undefined8 *)(pauVar23[7] + 8) = uVar20;
            *(undefined8 *)(pauVar23[7] + 0x10) = uVar21;
            *(undefined8 *)(pauVar23[7] + 0x18) = uVar22;
            pauVar23 = pauVar23 + 8;
            pauVar27 = pauVar27 + 8;
            param_3 = param_3 - 0x100;
          } while (0xff < param_3);
        }
      }
                    /* WARNING: Could not recover jumptable at 0x000180194982. Too many branches */
                    /* WARNING: Treating indirect jump as call */
      (*(code *)((ulonglong)*(uint *)(&DAT_1801c3360 + (param_3 + 0x1f >> 5) * 4) + 0x180000000))();
      return;
    }
    for (; param_3 != 0; param_3 = param_3 - 1) {
      *(undefined1 *)param_1 = *(undefined1 *)param_2;
      param_2 = (undefined8 *)((longlong)param_2 + 1);
      param_1 = (undefined8 *)((longlong)param_1 + 1);
    }
    return;
  }
  uVar10 = *param_2;
  uVar11 = param_2[1];
  lVar30 = (longlong)param_2 - (longlong)param_1;
  puVar26 = (undefined8 *)((longlong)param_1 + lVar30 + (param_3 - 0x10));
  uVar12 = *puVar26;
  uVar13 = puVar26[1];
  puVar25 = (undefined8 *)((longlong)param_1 + (param_3 - 0x10));
  uVar29 = param_3 - 0x10;
  puVar26 = puVar25;
  uVar32 = uVar12;
  uVar33 = uVar13;
  if (((ulonglong)puVar25 & 0xf) != 0) {
    puVar26 = (undefined8 *)((ulonglong)puVar25 & 0xfffffffffffffff0);
    uVar32 = *(undefined8 *)((longlong)puVar26 + lVar30);
    uVar33 = ((undefined8 *)((longlong)puVar26 + lVar30))[1];
    *puVar25 = uVar12;
    *(undefined8 *)((longlong)param_1 + (param_3 - 8)) = uVar13;
    uVar29 = (longlong)puVar26 - (longlong)param_1;
  }
  uVar31 = uVar29 >> 7;
  if (uVar31 != 0) {
    *puVar26 = uVar32;
    puVar26[1] = uVar33;
    puVar25 = puVar26;
    while( true ) {
      puVar1 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x10);
      uVar12 = puVar1[1];
      puVar26 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x20);
      uVar13 = *puVar26;
      uVar32 = puVar26[1];
      puVar26 = puVar25 + -0x10;
      puVar25[-2] = *puVar1;
      puVar25[-1] = uVar12;
      puVar25[-4] = uVar13;
      puVar25[-3] = uVar32;
      puVar1 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x30);
      uVar12 = puVar1[1];
      puVar2 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x40);
      uVar13 = *puVar2;
      uVar32 = puVar2[1];
      uVar31 = uVar31 - 1;
      puVar25[-6] = *puVar1;
      puVar25[-5] = uVar12;
      puVar25[-8] = uVar13;
      puVar25[-7] = uVar32;
      puVar1 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x50);
      uVar12 = puVar1[1];
      puVar2 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x60);
      uVar13 = *puVar2;
      uVar32 = puVar2[1];
      puVar25[-10] = *puVar1;
      puVar25[-9] = uVar12;
      puVar25[-0xc] = uVar13;
      puVar25[-0xb] = uVar32;
      puVar1 = (undefined8 *)((longlong)puVar25 + lVar30 + -0x70);
      uVar12 = *puVar1;
      uVar13 = puVar1[1];
      uVar32 = *(undefined8 *)((longlong)puVar26 + lVar30);
      uVar33 = ((undefined8 *)((longlong)puVar26 + lVar30))[1];
      if (uVar31 == 0) break;
      puVar25[-0xe] = uVar12;
      puVar25[-0xd] = uVar13;
      *puVar26 = uVar32;
      puVar25[-0xf] = uVar33;
      puVar25 = puVar26;
    }
    puVar25[-0xe] = uVar12;
    puVar25[-0xd] = uVar13;
    uVar29 = uVar29 & 0x7f;
  }
  for (uVar31 = uVar29 >> 4; uVar31 != 0; uVar31 = uVar31 - 1) {
    *puVar26 = uVar32;
    puVar26[1] = uVar33;
    puVar26 = puVar26 + -2;
    uVar32 = *(undefined8 *)((longlong)puVar26 + lVar30);
    uVar33 = ((undefined8 *)((longlong)puVar26 + lVar30))[1];
  }
  if ((uVar29 & 0xf) != 0) {
    *param_1 = uVar10;
    param_1[1] = uVar11;
  }
  *puVar26 = uVar32;
  puVar26[1] = uVar33;
  return;
}


```

## FUN_180194db0 at `180194db0`

```c

undefined1 (*) [32] FUN_180194db0(undefined1 (*param_1) [32],byte param_2,ulonglong param_3)

{
  undefined1 auVar1 [32];
  undefined1 (*pauVar2) [32];
  undefined1 (*pauVar3) [32];
  undefined1 (*pauVar4) [16];
  ulonglong uVar5;
  longlong lVar6;
  ulonglong uVar7;
  undefined1 uVar8;
  longlong lVar11;
  undefined1 auVar12 [16];
  undefined1 auVar13 [32];
  undefined2 uVar9;
  undefined4 uVar10;
  
  uVar5 = (ulonglong)param_2;
  lVar11 = uVar5 * 0x101010101010101;
  uVar8 = (undefined1)lVar11;
  uVar9 = (undefined2)lVar11;
  uVar10 = (undefined4)lVar11;
  switch(param_3) {
  case 0:
    return param_1;
  case 8:
    *(longlong *)(param_1[-1] + param_3 + 0x18) = lVar11;
    return param_1;
  case 9:
    *(longlong *)(param_1[-1] + param_3 + 0x17) = lVar11;
    param_1[-1][param_3 + 0x1f] = uVar8;
    return param_1;
  case 10:
    *(longlong *)(param_1[-1] + param_3 + 0x16) = lVar11;
    *(undefined2 *)(param_1[-1] + param_3 + 0x1e) = uVar9;
    return param_1;
  case 0xb:
    *(longlong *)(param_1[-1] + param_3 + 0x15) = lVar11;
    *(undefined2 *)(param_1[-1] + param_3 + 0x1d) = uVar9;
    param_1[-1][param_3 + 0x1f] = uVar8;
    return param_1;
  case 0xc:
    *(longlong *)(param_1[-1] + param_3 + 0x14) = lVar11;
  case 4:
    *(undefined4 *)(param_1[-1] + param_3 + 0x1c) = uVar10;
    return param_1;
  case 0xd:
    *(longlong *)(param_1[-1] + param_3 + 0x13) = lVar11;
  case 5:
    *(undefined4 *)(param_1[-1] + param_3 + 0x1b) = uVar10;
    param_1[-1][param_3 + 0x1f] = uVar8;
    return param_1;
  case 0xe:
    *(longlong *)(param_1[-1] + param_3 + 0x12) = lVar11;
  case 6:
    *(undefined4 *)(param_1[-1] + param_3 + 0x1a) = uVar10;
  case 2:
    *(undefined2 *)(param_1[-1] + param_3 + 0x1e) = uVar9;
    return param_1;
  case 0xf:
    *(longlong *)(param_1[-1] + param_3 + 0x11) = lVar11;
  case 7:
    *(undefined4 *)(param_1[-1] + param_3 + 0x19) = uVar10;
  case 3:
    *(undefined2 *)(param_1[-1] + param_3 + 0x1d) = uVar9;
  case 1:
    param_1[-1][param_3 + 0x1f] = uVar8;
    return param_1;
  }
  auVar12._8_8_ = lVar11;
  auVar12._0_8_ = lVar11;
  if (param_3 < 0x21) {
    *(undefined1 (*) [16])*param_1 = auVar12;
    *(undefined1 (*) [16])(param_1[-1] + param_3 + 0x10) = auVar12;
    return param_1;
  }
  pauVar2 = param_1;
  if (DAT_1801f4e80 < 3) {
    if ((param_3 <= DAT_1801f4e88) || (((byte)DAT_1801f8864 & 2) == 0)) {
      lVar11 = ((ulonglong)param_1 & 0xf) - 0x10;
      pauVar4 = (undefined1 (*) [16])((longlong)param_1 - lVar11);
      param_3 = param_3 + lVar11;
      if (0x80 < param_3) {
        do {
          *pauVar4 = auVar12;
          pauVar4[1] = auVar12;
          pauVar4[2] = auVar12;
          pauVar4[3] = auVar12;
          pauVar4[4] = auVar12;
          pauVar4[5] = auVar12;
          pauVar4[6] = auVar12;
          pauVar4[7] = auVar12;
          pauVar4 = pauVar4 + 8;
          param_3 = param_3 - 0x80;
        } while (0x7f < param_3);
      }
                    /* WARNING: Could not recover jumptable at 0x0001801950f8. Too many branches */
                    /* WARNING: Treating indirect jump as call */
      pauVar2 = (undefined1 (*) [32])
                (*(code *)((ulonglong)*(uint *)(&DAT_1801c3458 + (param_3 + 0xf >> 4) * 4) +
                          0x180000000))(pauVar4,uVar5 - lVar11);
      return pauVar2;
    }
  }
  else if (((param_3 <= DAT_1801f4e88) || (DAT_1801f4e90 < param_3)) ||
          (((byte)DAT_1801f8864 & 2) == 0)) {
    auVar13._16_16_ = auVar12;
    auVar13._0_16_ = auVar12;
    lVar6 = ((ulonglong)param_1 & 0x1f) - 0x20;
    pauVar2 = (undefined1 (*) [32])((longlong)param_1 - lVar6);
    param_3 = param_3 + lVar6;
    if (0x100 < param_3) {
      if (DAT_1801f4e90 < param_3) {
        do {
          uVar5 = param_3;
          pauVar3 = pauVar2;
          auVar1 = vmovntdq_avx(auVar13);
          *pauVar3 = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[1] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[2] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[3] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[4] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[5] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[6] = auVar1;
          auVar1 = vmovntdq_avx(auVar13);
          pauVar3[7] = auVar1;
          pauVar2 = pauVar3 + 8;
          param_3 = uVar5 - 0x100;
        } while (0xff < uVar5 - 0x100);
        uVar7 = uVar5 - 0xe1 & 0xffffffffffffffe0;
        switch(uVar5) {
        case 0x1e1:
        case 0x1e2:
        case 0x1e3:
        case 0x1e4:
        case 0x1e5:
        case 0x1e6:
        case 0x1e7:
        case 0x1e8:
        case 0x1e9:
        case 0x1ea:
        case 0x1eb:
        case 0x1ec:
        case 0x1ed:
        case 0x1ee:
        case 0x1ef:
        case 0x1f0:
        case 0x1f1:
        case 0x1f2:
        case 499:
        case 500:
        case 0x1f5:
        case 0x1f6:
        case 0x1f7:
        case 0x1f8:
        case 0x1f9:
        case 0x1fa:
        case 0x1fb:
        case 0x1fc:
        case 0x1fd:
        case 0x1fe:
        case 0x1ff:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(*pauVar3 + uVar7) = auVar1;
        case 0x1c1:
        case 0x1c2:
        case 0x1c3:
        case 0x1c4:
        case 0x1c5:
        case 0x1c6:
        case 0x1c7:
        case 0x1c8:
        case 0x1c9:
        case 0x1ca:
        case 0x1cb:
        case 0x1cc:
        case 0x1cd:
        case 0x1ce:
        case 0x1cf:
        case 0x1d0:
        case 0x1d1:
        case 0x1d2:
        case 0x1d3:
        case 0x1d4:
        case 0x1d5:
        case 0x1d6:
        case 0x1d7:
        case 0x1d8:
        case 0x1d9:
        case 0x1da:
        case 0x1db:
        case 0x1dc:
        case 0x1dd:
        case 0x1de:
        case 0x1df:
        case 0x1e0:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[1] + uVar7) = auVar1;
        case 0x1a1:
        case 0x1a2:
        case 0x1a3:
        case 0x1a4:
        case 0x1a5:
        case 0x1a6:
        case 0x1a7:
        case 0x1a8:
        case 0x1a9:
        case 0x1aa:
        case 0x1ab:
        case 0x1ac:
        case 0x1ad:
        case 0x1ae:
        case 0x1af:
        case 0x1b0:
        case 0x1b1:
        case 0x1b2:
        case 0x1b3:
        case 0x1b4:
        case 0x1b5:
        case 0x1b6:
        case 0x1b7:
        case 0x1b8:
        case 0x1b9:
        case 0x1ba:
        case 0x1bb:
        case 0x1bc:
        case 0x1bd:
        case 0x1be:
        case 0x1bf:
        case 0x1c0:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[2] + uVar7) = auVar1;
        case 0x181:
        case 0x182:
        case 0x183:
        case 0x184:
        case 0x185:
        case 0x186:
        case 0x187:
        case 0x188:
        case 0x189:
        case 0x18a:
        case 0x18b:
        case 0x18c:
        case 0x18d:
        case 0x18e:
        case 399:
        case 400:
        case 0x191:
        case 0x192:
        case 0x193:
        case 0x194:
        case 0x195:
        case 0x196:
        case 0x197:
        case 0x198:
        case 0x199:
        case 0x19a:
        case 0x19b:
        case 0x19c:
        case 0x19d:
        case 0x19e:
        case 0x19f:
        case 0x1a0:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[3] + uVar7) = auVar1;
        case 0x161:
        case 0x162:
        case 0x163:
        case 0x164:
        case 0x165:
        case 0x166:
        case 0x167:
        case 0x168:
        case 0x169:
        case 0x16a:
        case 0x16b:
        case 0x16c:
        case 0x16d:
        case 0x16e:
        case 0x16f:
        case 0x170:
        case 0x171:
        case 0x172:
        case 0x173:
        case 0x174:
        case 0x175:
        case 0x176:
        case 0x177:
        case 0x178:
        case 0x179:
        case 0x17a:
        case 0x17b:
        case 0x17c:
        case 0x17d:
        case 0x17e:
        case 0x17f:
        case 0x180:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[4] + uVar7) = auVar1;
        case 0x141:
        case 0x142:
        case 0x143:
        case 0x144:
        case 0x145:
        case 0x146:
        case 0x147:
        case 0x148:
        case 0x149:
        case 0x14a:
        case 0x14b:
        case 0x14c:
        case 0x14d:
        case 0x14e:
        case 0x14f:
        case 0x150:
        case 0x151:
        case 0x152:
        case 0x153:
        case 0x154:
        case 0x155:
        case 0x156:
        case 0x157:
        case 0x158:
        case 0x159:
        case 0x15a:
        case 0x15b:
        case 0x15c:
        case 0x15d:
        case 0x15e:
        case 0x15f:
        case 0x160:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[5] + uVar7) = auVar1;
        case 0x121:
        case 0x122:
        case 0x123:
        case 0x124:
        case 0x125:
        case 0x126:
        case 0x127:
        case 0x128:
        case 0x129:
        case 0x12a:
        case 299:
        case 300:
        case 0x12d:
        case 0x12e:
        case 0x12f:
        case 0x130:
        case 0x131:
        case 0x132:
        case 0x133:
        case 0x134:
        case 0x135:
        case 0x136:
        case 0x137:
        case 0x138:
        case 0x139:
        case 0x13a:
        case 0x13b:
        case 0x13c:
        case 0x13d:
        case 0x13e:
        case 0x13f:
        case 0x140:
          auVar1 = vmovntdq_avx(auVar13);
          *(undefined1 (*) [32])(pauVar3[6] + uVar7) = auVar1;
        default:
          *(undefined1 (*) [32])(pauVar3[-1] + uVar5) = auVar13;
        case 0x100:
          *param_1 = auVar13;
          return param_1;
        }
      }
      do {
        *pauVar2 = auVar13;
        pauVar2[1] = auVar13;
        pauVar2[2] = auVar13;
        pauVar2[3] = auVar13;
        pauVar2[4] = auVar13;
        pauVar2[5] = auVar13;
        pauVar2[6] = auVar13;
        pauVar2[7] = auVar13;
        pauVar2 = pauVar2 + 8;
        param_3 = param_3 - 0x100;
      } while (0xff < param_3);
    }
                    /* WARNING: Could not recover jumptable at 0x000180194f44. Too many branches */
                    /* WARNING: Treating indirect jump as call */
    pauVar2 = (undefined1 (*) [32])
              (*(code *)((ulonglong)*(uint *)(&DAT_1801c3410 + (param_3 + 0x1f >> 5) * 4) +
                        0x180000000))(lVar11,uVar5 - lVar6);
    return pauVar2;
  }
  for (; param_3 != 0; param_3 = param_3 - 1) {
    (*pauVar2)[0] = param_2;
    pauVar2 = (undefined1 (*) [32])(*pauVar2 + 1);
  }
  return param_1;
}


```

