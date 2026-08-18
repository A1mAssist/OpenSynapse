# Effect direct static evidence

Vftable: `1801acb18`

## Vftable slots

- slot 0: `180066fb0` `FUN_180066fb0`, size `0x2b`
- slot 1: `180066b40` `FUN_180066b40`, size `0x213`
- slot 2: `180066d60` `FUN_180066d60`, size `0x1bc`
- slot 3: `180066f30` `FUN_180066f30`, size `0x62`
- slot 4: `180066fa0` `FUN_180066fa0`, size `0xd`
- slot 5: `180069260` `FUN_180069260`, size `0x24`
- slot 6: `1800692c0` `FUN_1800692c0`, size `0x6`
- slot 7: `1800692d0` `FUN_1800692d0`, size `0x6`
- slot 8: `180051990` `FUN_180051990`, size `0x3`
- slot 9: `180069290` `FUN_180069290`, size `0x22`

## Vftable references

- `180066a8d` in `FUN_180066a80`
- `180066a94` in `FUN_180066a80`
- `180066ad6` in `FUN_180066ac0`
- `180066add` in `FUN_180066ac0`

## Requested helpers

- `180066f20` `FUN_180066f20`
- `18014c780` `FUN_18014c780`
- data `1801acae0`: hex `00000000210000004200000064000000`, int32 `0`, float32 `0.0`, float64 `7.0025861102E-313`
- data `1801acae8`: hex `4200000064000000FF00000000FF0000`, int32 `66`, float32 `9.2E-44`, float64 `2.12199579129E-312`
- data `1801acaf0`: hex `FF00000000FF00000000FF00FF000000`, int32 `255`, float32 `3.57E-43`, float64 `1.38523885234339E-309`
- data `1801acaf8`: hex `0000FF00FF00000000C8AF4800000000`, int32 `16711680`, float32 `2.3418052E-38`, float64 `5.41117183363E-312`
- data `1801acb00`: hex `00C8AF4800000000182D4454FB210940`, int32 `1219479552`, float32 `360000.0`, float64 `6.025029524E-315`
- data `1801aca00`: hex `182D4454FB2119400000000000807640`, int32 `1413754136`, float32 `3.3702806E12`, float64 `6.283185307179586`
- data `1801acb08`: hex `182D4454FB21094070CB1A8001000000`, int32 `1413754136`, float32 `3.3702806E12`, float64 `3.141592653589793`

## FUN_180066fb0 at `180066fb0`

```c

undefined8 FUN_180066fb0(undefined8 param_1,int param_2)

{
  FUN_180066ac0();
  if (param_2 != 0) {
    FUN_1800b9d98(param_1,0x1b0);
  }
  return param_1;
}


```

## FUN_180066b40 at `180066b40`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_180066b40(longlong param_1,longlong param_2,undefined8 param_3,undefined8 param_4,
                       undefined4 param_5,undefined8 param_6)

{
  undefined8 uVar1;
  ulonglong uVar2;
  void *pvVar3;
  uint uVar4;
  uint uVar5;
  bool bVar6;

  uVar2 = FUN_1800691a0(param_1,param_2,param_3,param_4,param_5,param_6);
  if (-1 < (int)uVar2) {
    uVar2 = uVar2 & 0xffffffff;
    *(undefined4 *)(param_1 + 0x14) = *(undefined4 *)(param_1 + 0x98);
    uVar1 = _UNK_1801acae8;
    if ((100 < *(uint *)(param_1 + 0x44)) ||
       ((*(uint *)(param_1 + 0x44) != 100 &&
        ((100 < *(uint *)(param_1 + 0x48) ||
         ((*(uint *)(param_1 + 0x48) != 100 &&
          ((100 < *(uint *)(param_1 + 0x4c) ||
           ((*(uint *)(param_1 + 0x4c) != 100 &&
            ((100 < *(uint *)(param_1 + 0x50) ||
             ((*(uint *)(param_1 + 0x50) != 100 &&
              ((100 < *(uint *)(param_1 + 0x54) ||
               ((*(uint *)(param_1 + 0x54) != 100 &&
                ((100 < *(uint *)(param_1 + 0x58) ||
                 ((*(uint *)(param_1 + 0x58) != 100 &&
                  ((100 < *(uint *)(param_1 + 0x5c) ||
                   ((*(uint *)(param_1 + 0x5c) != 100 &&
                    ((100 < *(uint *)(param_1 + 0x60) ||
                     ((*(uint *)(param_1 + 0x60) != 100 && (*(int *)(param_1 + 100) != 100))))))))))
                 )))))))))))))))))))))) {
      *(undefined8 *)(param_1 + 0x40) = _DAT_1801acae0;
      *(undefined8 *)(param_1 + 0x48) = uVar1;
      uVar1 = _UNK_1801acaf8;
      *(undefined8 *)(param_1 + 0x68) = _DAT_1801acaf0;
      *(undefined8 *)(param_1 + 0x70) = uVar1;
    }
    *(undefined4 *)(param_1 + 0x18c) = *(undefined4 *)(param_1 + 0x94);
    uVar4 = (uint)(DAT_1801acb00 / (float)*(uint *)(param_1 + 0x90));
    if (*(uint *)(param_1 + 0x90) == 0) {
      uVar4 = 0;
    }
    *(int *)(param_1 + 0x184) =
         (int)((ulonglong)uVar4 / (1000 / (ulonglong)*(uint *)(param_1 + 0x38)));
    uVar4 = *(uint *)(param_1 + 0x9c);
    if (0xff < uVar4) {
      *(undefined4 *)(param_1 + 0x9c) = 0;
      uVar4 = 0;
    }
    uVar5 = *(uint *)(param_1 + 0xa0);
    if (0xff < uVar5) {
      *(undefined4 *)(param_1 + 0xa0) = 0;
      uVar5 = 0;
    }
    *(uint *)(param_1 + 0x198) = (uVar4 * 0x28) / *(uint *)(param_2 + 0xf0);
    *(uint *)(param_1 + 0x19c) = (uVar5 * 0x28) / *(uint *)(param_2 + 0xec);
    *(undefined8 *)(param_1 + 0x1a8) = 0x40000000400;
    pvVar3 = operator_new(0x1000);
    *(void **)(param_1 + 0x1a0) = pvVar3;
    FUN_1800692e0(param_1,10,(undefined8 *)(param_1 + 0x40),param_1 + 0x68,0x400,pvVar3);
    uVar4 = *(uint *)(param_1 + 0x14);
    if ((uVar4 & 0x1000) != 0) {
      FUN_18004cde0(param_2,1);
      uVar4 = *(uint *)(param_1 + 0x14);
    }
    bVar6 = (uVar4 & 0x1000) == 0;
    if (!bVar6) {
      *(undefined4 *)(param_1 + 0x194) = 0;
    }
    *(uint *)(param_1 + 400) = (uint)bVar6;
  }
  return uVar2;
}


```

## FUN_180066d60 at `180066d60`

```c

undefined8 FUN_180066d60(longlong param_1,longlong param_2)

{
  int iVar1;
  int iVar2;
  double dVar3;
  double dVar4;
  undefined8 uVar5;
  int iVar6;
  uint uVar7;
  int iVar8;
  int iVar9;
  int iVar10;
  float fVar11;
  double dVar12;

  if (param_2 == 0) {
    uVar5 = 0x80004005;
  }
  else {
    uVar5 = 0;
    if (*(int *)(param_1 + 400) != 0) {
      iVar10 = *(int *)(param_1 + 0x18c);
      if (iVar10 == 0) {
        *(undefined4 *)(param_1 + 400) = 0;
      }
      else {
        uVar7 = *(uint *)(param_1 + 0x184);
        iVar8 = 0;
        if (uVar7 != 0) {
          fVar11 = ((float)*(uint *)(param_1 + 0x188) * (float)*(uint *)(param_1 + 0x1ac)) /
                   (float)uVar7;
          if ((*(uint *)(param_1 + 0x14) & 0x400) != 0) {
            fVar11 = (float)*(uint *)(param_1 + 0x1ac) - fVar11;
          }
          iVar8 = (int)fVar11;
          uVar7 = (*(uint *)(param_1 + 0x188) + 1) % uVar7;
          *(uint *)(param_1 + 0x188) = uVar7;
          if (iVar10 != -1 && uVar7 == 0) {
            *(int *)(param_1 + 0x18c) = iVar10 + -1;
          }
        }
        dVar4 = DAT_1801acb08;
        dVar3 = DAT_1801aca00;
        uVar5 = 0;
        iVar10 = *(int *)(param_1 + 0x18);
        if ((0 < iVar10) && (iVar6 = *(int *)(param_1 + 0x1c), 0 < iVar6)) {
          iVar1 = *(int *)(*(longlong *)(param_1 + 8) + 0xdc);
          iVar2 = *(int *)(param_1 + 0x198);
          iVar9 = 0;
          do {
            if (0 < iVar6) {
              iVar10 = 0;
              do {
                dVar12 = (double)FUN_180066f20((float)(iVar9 + (iVar1 - iVar2)));
                *(undefined4 *)
                 (param_2 + (ulonglong)(uint)(*(int *)(param_1 + 0x1c) * iVar9 + iVar10) * 4) =
                     *(undefined4 *)
                      (*(longlong *)(param_1 + 0x1a0) +
                      (longlong)
                      (int)((uint)((int)(((double)*(uint *)(param_1 + 0x1ac) * (dVar12 + dVar4)) /
                                        dVar3) + iVar8) % *(uint *)(param_1 + 0x1ac)) * 4);
                iVar10 = iVar10 + 1;
                iVar6 = *(int *)(param_1 + 0x1c);
              } while (iVar10 < iVar6);
              iVar10 = *(int *)(param_1 + 0x18);
            }
            iVar9 = iVar9 + 1;
          } while (iVar9 < iVar10);
          uVar5 = 0;
        }
      }
    }
  }
  return uVar5;
}


```

## FUN_180066f30 at `180066f30`

```c

undefined8
FUN_180066f30(longlong param_1,undefined8 param_2,undefined8 param_3,int param_4,int param_5)

{
  undefined4 uVar1;

  if ((((param_5 == 0) || ((*(uint *)(param_1 + 0x14) & 0x1000) == 0)) ||
      (*(int *)(param_1 + 0x194) == param_4)) || (*(int *)(param_1 + 0x194) = param_4, param_4 == 0)
     ) {
    return 0;
  }
  if ((*(uint *)(param_1 + 0x14) & 4) == 0) {
    if (*(int *)(param_1 + 400) == 1) {
      return 0;
    }
  }
  else {
    uVar1 = 0;
    if (*(int *)(param_1 + 400) == 1) goto LAB_180066f7f;
  }
  *(undefined4 *)(param_1 + 400) = 1;
  uVar1 = *(undefined4 *)(param_1 + 0x94);
LAB_180066f7f:
  *(undefined4 *)(param_1 + 0x18c) = uVar1;
  *(undefined4 *)(param_1 + 0x188) = 0;
  return 0;
}


```

## FUN_180066fa0 at `180066fa0`

```c

undefined8 FUN_180066fa0(longlong param_1)

{
  *(undefined4 *)(param_1 + 0x188) = 0;
  return 0;
}


```

## FUN_180069260 at `180069260`

```c

undefined8 FUN_180069260(longlong *param_1,undefined4 param_2,undefined4 param_3)

{
  *(undefined4 *)(param_1 + 3) = param_2;
  *(undefined4 *)((longlong)param_1 + 0x1c) = param_3;
  (**(code **)(*param_1 + 0x48))(param_1,0,0,param_3,param_2);
  return 0;
}


```

## FUN_1800692c0 at `1800692c0`

```c

undefined8 FUN_1800692c0(void)

{
  return 0x80004005;
}


```

## FUN_1800692d0 at `1800692d0`

```c

undefined8 FUN_1800692d0(void)

{
  return 0x80004005;
}


```

## FUN_180051990 at `180051990`

```c

undefined8 FUN_180051990(void)

{
  return 0;
}


```

## FUN_180069290 at `180069290`

```c

undefined8 FUN_180069290(longlong param_1,int param_2,int param_3,int param_4,int param_5)

{
  *(int *)(param_1 + 0x20) = param_2;
  *(int *)(param_1 + 0x24) = param_3;
  *(int *)(param_1 + 0x28) = param_4;
  *(int *)(param_1 + 0x2c) = param_5;
  *(int *)(param_1 + 0x30) = param_5 - param_3;
  *(int *)(param_1 + 0x34) = param_4 - param_2;
  return 0;
}


```

## FUN_180066a80 at `180066a80`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_180066a80(undefined8 *param_1)

{
  undefined8 uVar1;

  FUN_180069140();
  *param_1 = CColorWheelEffect::vftable;
  uVar1 = _UNK_1801ac688;
  param_1[0x30] = _DAT_1801ac680;
  param_1[0x31] = uVar1;
  param_1[0x34] = 0;
  param_1[0x35] = 0;
  return param_1;
}


```

## FUN_180066ac0 at `180066ac0`

```c

void FUN_180066ac0(undefined8 *param_1,undefined8 param_2,undefined8 param_3,undefined8 param_4)

{
  *param_1 = CColorWheelEffect::vftable;
  if ((param_1[1] != 0) && ((*(byte *)((longlong)param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_1[1],0,param_3,param_4,0xfffffffffffffffe);
  }
  if (param_1[0x34] != 0) {
    FUN_1800b9de0();
  }
  FUN_180069190(param_1);
  return;
}


```

## FUN_180066f20 at `180066f20`

```c

void FUN_180066f20(float param_1,int param_2)

{
  FUN_18014c780((double)param_1,(double)param_2);
  return;
}


```

## FUN_18014c780 at `18014c780`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

double FUN_18014c780(undefined8 param_1,undefined8 param_2)

{
  undefined1 auVar1 [16];
  undefined1 auVar2 [16];
  undefined1 auVar3 [16];
  undefined1 auVar4 [16];
  undefined1 auVar5 [16];
  undefined1 auVar6 [16];
  bool bVar7;
  double dVar8;
  double dVar9;
  uint uVar10;
  int iVar11;
  double dVar12;
  int iVar13;
  ulonglong uVar14;
  uint uVar15;
  double dVar16;
  undefined1 auVar17 [16];
  undefined1 auVar18 [16];
  undefined1 auVar19 [16];
  undefined1 auVar20 [16];
  undefined1 auVar21 [16];
  undefined1 auVar22 [16];
  undefined1 auVar23 [16];
  undefined1 in_ZMM0 [64];
  ulonglong uVar24;
  undefined1 auVar25 [16];
  undefined1 auVar26 [16];
  undefined1 auVar27 [16];
  undefined1 auVar28 [16];
  undefined1 auVar29 [16];
  undefined1 in_ZMM1 [64];
  double dVar30;
  undefined1 auVar31 [16];
  undefined1 auVar32 [16];
  undefined1 auVar33 [16];
  undefined1 auVar34 [16];
  undefined1 auVar35 [16];
  undefined1 auVar36 [16];
  undefined1 auVar37 [16];
  undefined1 auVar38 [16];
  undefined1 auVar39 [16];
  undefined1 auVar40 [16];
  undefined1 auVar41 [16];
  undefined1 auVar42 [16];
  undefined1 auVar43 [16];
  undefined1 auVar44 [16];
  ulonglong uVar46;
  undefined1 auVar45 [64];
  undefined1 auVar47 [16];
  undefined1 auVar48 [64];

  if (((byte)DAT_1801f9b54 & 3) != 3) {
    auVar41._0_8_ = (double)FUN_18014c9d0();
    return auVar41._0_8_;
  }
  uVar24 = in_ZMM1._0_8_;
  dVar16 = in_ZMM0._0_8_;
  uVar15 = in_ZMM0._4_4_ >> 0x14 & 0x7ff;
  uVar10 = in_ZMM1._4_4_ >> 0x14 & 0x7ff;
  iVar11 = uVar15 - uVar10;
  uVar14 = uVar24 & 0x7fffffffffffffff;
  auVar41._0_8_ = ABS(dVar16);
  auVar25 = in_ZMM1._0_16_;
  auVar48 = ZEXT1664(auVar25);
  auVar17 = in_ZMM0._0_16_;
  auVar45 = ZEXT1664(auVar17);
  if (0x7ff0000000000000 < uVar14) {
    auVar41._0_8_ = (double)FUN_1801907d0(uVar24,param_2,0);
    return auVar41._0_8_;
  }
  if (0x7ff0000000000000 < (ulonglong)auVar41._0_8_) {
    auVar41._0_8_ = (double)FUN_1801907d0(dVar16,param_2,0);
    return auVar41._0_8_;
  }
  if (auVar41._0_8_ == 0.0) {
    if (-1 < (longlong)uVar24) {
      return dVar16;
    }
  }
  else {
    if (uVar14 == 0) {
      FUN_18017c590();
      auVar17 = auVar45._0_16_;
      auVar25 = auVar48._0_16_;
      if ((longlong)dVar16 < 0) {
        return DAT_1801b4e30;
      }
    }
    if ((uVar10 < 0x3fd) && (uVar15 < 0x3fd)) {
      if ((uVar24 & 0x7ff0000000000000) == 0) {
        if ((longlong)uVar24 < 0) {
          dVar30 = (double)(uVar24 | 0x4010000000000000) + DAT_1801ad048;
        }
        else {
          dVar30 = (double)(uVar24 | 0x4010000000000000) + DAT_1801b4e48;
        }
      }
      else {
        dVar30 = (double)(uVar24 + 0x4000000000000000);
      }
      if (((ulonglong)dVar16 & 0x7ff0000000000000) == 0) {
        if ((longlong)dVar16 < 0) {
          dVar12 = (double)((ulonglong)dVar16 | 0x4010000000000000) + DAT_1801ad048;
        }
        else {
          dVar12 = (double)((ulonglong)dVar16 | 0x4010000000000000) + DAT_1801b4e48;
        }
      }
      else {
        dVar12 = (double)((longlong)dVar16 + 0x4000000000000000);
      }
      auVar17._8_8_ = 0;
      auVar17._0_8_ = (ulonglong)dVar12;
      auVar25._8_8_ = 0;
      auVar25._0_8_ = dVar30;
      iVar11 = ((uint)((ulonglong)dVar12 >> 0x34) & 0x7ff) -
               ((uint)((ulonglong)dVar30 >> 0x34) & 0x7ff);
    }
    if (0x38 < iVar11) {
      FUN_18017c590(0x20);
      if ((longlong)dVar16 < 0) {
        return DAT_1801b4e30;
      }
      return DAT_1801b4e00;
    }
    dVar30 = auVar25._0_8_;
    dVar12 = auVar17._0_8_;
    uVar46 = auVar17._8_8_;
    if ((iVar11 < -0x1c) && (-1 < (longlong)uVar24)) {
      if (iVar11 < -0x432) {
        FUN_18017c590(0x20);
        if ((longlong)dVar16 < 0) {
          return DAT_1801b4e20;
        }
        return 0.0;
      }
      if (-0x3ff < iVar11) {
        return dVar12 / dVar30;
      }
      dVar30 = (dVar12 * 1.2676506002282294e+30) / dVar30;
      uVar14 = (ulonglong)ABS(dVar30) >> 0x34;
      uVar10 = (uint)((ulonglong)ABS(dVar30) >> 0x34);
      if (uVar10 < 0x65) {
        if ((int)(0x65 - uVar10) < 0x37) {
          uVar14 = ((ulonglong)dVar30 & 0x1fffffffffffff | 0x10000000000000) >>
                   (100 - uVar14 & 0x3f);
          uVar14 = (uVar14 >> 1) + (ulonglong)((uint)uVar14 & 1);
        }
        else {
          uVar14 = 0;
        }
      }
      else {
        uVar14 = uVar14 - 100 << 0x34 | (ulonglong)dVar30 & 0xfffffffffffff;
      }
      auVar41._0_8_ = (double)((ulonglong)dVar30 & 0x8000000000000000 | uVar14);
      if ((uVar14 & 0x7ff0000000000000) != 0) {
        return auVar41._0_8_;
      }
      FUN_18017c590(0x20);
      return auVar41._0_8_;
    }
    if ((-0x39 < iVar11) || (-1 < (longlong)uVar24)) {
      if ((auVar41._0_8_ != INFINITY) || (uVar14 != 0x7ff0000000000000)) {
        auVar47 = auVar25;
        if ((longlong)uVar24 < 0) {
          auVar47._0_8_ = (ulonglong)dVar30 ^ DAT_18019d6c0;
          auVar47._8_8_ = auVar25._8_8_ ^ _UNK_18019d6c8;
        }
        if ((longlong)dVar16 < 0) {
          auVar17._0_8_ = (ulonglong)dVar12 ^ DAT_18019d6c0;
          auVar17._8_8_ = uVar46 ^ _UNK_18019d6c8;
        }
        bVar7 = auVar47._0_8_ < auVar17._0_8_;
        auVar31._0_8_ = -(ulonglong)!bVar7;
        auVar31._8_8_ = 0xffffffffffffffff;
        auVar25 = vblendvpd_avx(auVar47,auVar17,auVar31);
        auVar32._0_8_ = -(ulonglong)!bVar7;
        auVar32._8_8_ = 0xffffffffffffffff;
        auVar17 = vblendvpd_avx(auVar17,auVar47,auVar32);
        dVar30 = auVar17._0_8_;
        auVar41._0_8_ = auVar25._0_8_ / dVar30;
        auVar41._8_8_ = auVar25._8_8_;
        if (auVar41._0_8_ <= DAT_1801b4db8) {
          dVar12 = 0.0;
          if (DAT_1801b4da0 <= auVar41._0_8_) {
            dVar8 = auVar41._0_8_ * auVar41._0_8_;
            auVar27._8_8_ = 0;
            auVar27._0_8_ = (ulonglong)auVar41._0_8_ & 0xffffffff00000000;
            auVar35._8_8_ = 0;
            auVar35._0_8_ = (ulonglong)dVar30 & 0xffffffff00000000;
            auVar25 = vfnmadd231sd_fma(auVar25,auVar35,auVar27);
            auVar22._8_8_ = 0;
            auVar22._0_8_ = dVar30 - (double)((ulonglong)dVar30 & 0xffffffff00000000);
            auVar25 = vfnmadd231sd_fma(auVar25,auVar27,auVar22);
            auVar28._8_8_ = 0;
            auVar28._0_8_ = auVar41._0_8_ - (double)((ulonglong)auVar41._0_8_ & 0xffffffff00000000);
            auVar25 = vfnmadd231sd_fma(auVar25,auVar17,auVar28);
            auVar29._8_8_ = 0;
            auVar29._0_8_ = DAT_1801b4dc0;
            auVar3._8_8_ = 0;
            auVar3._0_8_ = DAT_1801b4dc8;
            auVar39._8_8_ = 0;
            auVar39._0_8_ = dVar8;
            auVar17 = vfnmadd213sd_fma(auVar29,auVar39,auVar3);
            auVar4._8_8_ = 0;
            auVar4._0_8_ = DAT_1801b4dd0;
            auVar17 = vfnmadd213sd_fma(auVar17,auVar39,auVar4);
            auVar5._8_8_ = 0;
            auVar5._0_8_ = DAT_1801b4de0;
            auVar40._8_8_ = 0;
            auVar40._0_8_ = dVar8;
            auVar17 = vfnmadd213sd_fma(auVar17,auVar40,auVar5);
            auVar6._8_8_ = 0;
            auVar6._0_8_ = DAT_1801b4df0;
            auVar17 = vfnmadd213sd_fma(auVar17,auVar40,auVar6);
            auVar36._0_8_ = auVar25._0_8_ / dVar30;
            auVar36._8_8_ = auVar25._8_8_;
            auVar23._8_8_ = 0;
            auVar23._0_8_ = dVar8 * auVar41._0_8_;
            auVar17 = vfnmadd231sd_fma(auVar36,auVar23,auVar17);
            auVar41._0_8_ = auVar17._0_8_ + auVar41._0_8_;
          }
        }
        else {
          auVar18._8_8_ = 0;
          auVar18._0_8_ = DAT_1801b4e18;
          auVar1._8_8_ = 0;
          auVar1._0_8_ = DAT_1801ad040;
          auVar41 = vfmadd213sd_fma(auVar18,auVar41,auVar1);
          uVar10 = (int)auVar41._0_8_ - 0x10;
          dVar12 = *(double *)(&DAT_1801b9050 + (ulonglong)uVar10 * 8);
          auVar41._0_8_ = (double)(uint)(int)auVar41._0_8_ * DAT_1801b4db0;
          iVar13 = 0x3ff - (auVar17._4_4_ >> 0x14 & 0x7ff);
          iVar11 = iVar13 / 2;
          dVar9 = (double)((longlong)iVar11 + 0x3ff << 0x34);
          dVar8 = (double)((longlong)(iVar13 - iVar11) + 0x3ff << 0x34);
          dVar30 = dVar30 * dVar9 * dVar8;
          auVar33._8_8_ = 0;
          auVar33._0_8_ = auVar25._0_8_ * dVar9 * dVar8;
          auVar26._8_8_ = 0;
          auVar26._0_8_ = (ulonglong)dVar30 & 0xfffffffff8000000;
          auVar42._8_8_ = 0;
          auVar42._0_8_ = auVar41._0_8_;
          auVar17 = vfnmadd231sd_fma(auVar33,auVar26,auVar42);
          auVar19._8_8_ = 0;
          auVar19._0_8_ = dVar30 - (double)((ulonglong)dVar30 & 0xfffffffff8000000);
          auVar17 = vfnmadd231sd_fma(auVar17,auVar42,auVar19);
          auVar20._8_8_ = 0;
          auVar20._0_8_ = DAT_1801b4dd8;
          auVar37._8_8_ = 0;
          auVar37._0_8_ = dVar30;
          auVar43._8_8_ = 0;
          auVar43._0_8_ = auVar41._0_8_;
          auVar41 = vfmadd231sd_fma(auVar37,auVar33,auVar43);
          auVar38._0_8_ = auVar17._0_8_ / auVar41._0_8_;
          auVar38._8_8_ = auVar17._8_8_;
          auVar2._8_8_ = 0;
          auVar2._0_8_ = DAT_1801b4de8;
          auVar34._8_8_ = 0;
          auVar34._0_8_ = auVar38._0_8_ * auVar38._0_8_;
          auVar41 = vfnmadd213sd_fma(auVar20,auVar34,auVar2);
          auVar21._8_8_ = 0;
          auVar21._0_8_ = auVar41._0_8_ * auVar38._0_8_ * auVar38._0_8_;
          auVar44._8_8_ = 0;
          auVar44._0_8_ = auVar38._0_8_ + *(double *)(&DAT_1801b97e0 + (ulonglong)uVar10 * 8);
          auVar41 = vfnmadd231sd_fma(auVar44,auVar38,auVar21);
        }
        if (bVar7) {
          dVar12 = DAT_1801b4e00 - dVar12;
          auVar41._0_8_ = DAT_1801b4d98 - auVar41._0_8_;
        }
        if ((longlong)uVar24 < 0) {
          dVar12 = DAT_1801b4e10 - dVar12;
          auVar41._0_8_ = DAT_1801b4da8 - auVar41._0_8_;
        }
        if (-1 < (longlong)dVar16) {
          return dVar12 + auVar41._0_8_;
        }
        return (double)((ulonglong)(dVar12 + auVar41._0_8_) ^ DAT_18019d6c0);
      }
      FUN_18017c590(0x20);
      if (-1 < (longlong)uVar24) {
        if (-1 < (longlong)dVar16) {
          return DAT_1801b4df8;
        }
        return DAT_1801b4e28;
      }
      if (-1 < (longlong)dVar16) {
        return DAT_1801b4e08;
      }
      return DAT_1801b4e38;
    }
  }
  FUN_18017c590(0x20);
  auVar41._0_8_ = DAT_1801acb08;
  if ((longlong)dVar16 < 0) {
    auVar41._0_8_ = DAT_1801b4e40;
  }
  return auVar41._0_8_;
}


```
