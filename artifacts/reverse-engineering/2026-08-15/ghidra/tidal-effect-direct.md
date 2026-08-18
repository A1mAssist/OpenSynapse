# CTidalEffect direct static evidence

Vftable: `1801acc48`

## Vftable slots

- slot 0: `180068780` `FUN_180068780`, size `0x2b`
- slot 1: `1800673b0` `FUN_1800673b0`, size `0x209`
- slot 2: `1800685a0` `FUN_1800685a0`, size `0xab`
- slot 3: `180068650` `FUN_180068650`, size `0xec`
- slot 4: `180068740` `FUN_180068740`, size `0x31`
- slot 5: `180069260` `FUN_180069260`, size `0x24`
- slot 6: `1800692c0` `FUN_1800692c0`, size `0x6`
- slot 7: `1800692d0` `FUN_1800692d0`, size `0x6`
- slot 8: `180051990` `FUN_180051990`, size `0x3`
- slot 9: `1800675c0` `FUN_1800675c0`, size `0x26f`

## Vftable references

- `1800671cb` in `FUN_1800671b0`
- `1800671d2` in `FUN_1800671b0`
- `1800672a8` in `FUN_180067290`
- `1800672af` in `FUN_180067290`

## Effect-specific helpers

- `180067830` `FUN_180067830`, size `0x130`
- `180067960` `FUN_180067960`, size `0x486`
- `180067e50` `FUN_180067e50`, size `0xeb`
- `180067f40` `FUN_180067f40`, size `0x18f`
- `1800680f0` `FUN_1800680f0`, size `0x1ed`
- `180068300` `FUN_180068300`, size `0x287`

## Referenced constants

- `1801ac680`: hex `000000000000000000000000FFFFFFFF`, int32 `0`, float32 `0.0`, float64 `0.0`
- `1801ac688`: hex `00000000FFFFFFFFF0C61A8001000000`, int32 `0`, float32 `0.0`, float64 `NaN`
- `1801acb08`: hex `182D4454FB21094070CB1A8001000000`, int32 `1413754136`, float32 `3.3702806E12`, float64 `3.141592653589793`
- `1801acbd0`: hex `00000000210000004200000064000000`, int32 `0`, float32 `0.0`, float64 `7.0025861102E-313`
- `1801acbd8`: hex `4200000064000000FF00000000FF0000`, int32 `66`, float32 `9.2E-44`, float64 `2.12199579129E-312`
- `1801acbe0`: hex `FF00000000FF00000000FF00FF000000`, int32 `255`, float32 `3.57E-43`, float64 `1.38523885234339E-309`
- `1801acbe4`: hex `00FF00000000FF00FF00000000000000`, int32 `65280`, float32 `9.1477E-41`, float64 `7.063274456498103E-304`
- `1801acbe8`: hex `0000FF00FF0000000000000000806640`, int32 `16711680`, float32 `2.3418052E-38`, float64 `5.41117183363E-312`
- `1801acbec`: hex `FF000000000000000080664000000000`, int32 `255`, float32 `3.57E-43`, float64 `1.26E-321`
- `1801acbf0`: hex `00000000008066400000000000000000`, int32 `0`, float32 `0.0`, float64 `180.0`
- `1801acc00`: hex `00000000000030430000000000003043`, int32 `0`, float32 `0.0`, float64 `4.503599627370496E15`
- `1801acc10`: hex `00000000000000C00000000000000000`, int32 `0`, float32 `0.0`, float64 `-2.0`
- `1801acc20`: hex `000000000000F03F000000000000F03F`, int32 `0`, float32 `0.0`, float64 `1.0`
- `1801acc30`: hex `000000000000F0BF0000000000000000`, int32 `0`, float32 `0.0`, float64 `-1.0`
- `1801a93f0`: hex `FFFFFFFFFFFFFF7FFFFFFFFFFFFFFF7F`, int32 `-1`, float32 `NaN`, float64 `NaN`
- `1801a9690`: hex `0000803F404A373CE4BACF11BF7D00AA`, int32 `1065353216`, float32 `1.0`, float64 `1.262555752621304E-18`
- `1801a9a60`: hex `000000000000803F0000000000000000`, int32 `0`, float32 `0.0`, float64 `0.0078125`
- `1801a9a70`: hex `0000C842000000000000000000000000`, int32 `1120403456`, float32 `100.0`, float64 `5.53552857E-315`
- `1801aca10`: hex `000000000000594017B7D13800000000`, int32 `0`, float32 `0.0`, float64 `100.0`
- `1801aadd8`: hex `000000000000F03FFFFFFF7FFFFFFF7F`, int32 `0`, float32 `0.0`, float64 `1.0`

## FUN_180068780 at `180068780`

```c

undefined8 FUN_180068780(undefined8 param_1,int param_2)

{
  FUN_180067290();
  if (param_2 != 0) {
    FUN_1800b9d98(param_1,0x248);
  }
  return param_1;
}


```

## FUN_1800673b0 at `1800673b0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_1800673b0(longlong *param_1,undefined8 param_2,undefined4 param_3,undefined4 param_4,
                       undefined8 param_5,undefined8 param_6)

{
  uint uVar1;
  longlong lVar2;
  undefined4 uVar3;
  undefined4 uVar4;
  uint uVar5;
  ulonglong uVar6;
  undefined4 uVar7;

  uVar6 = FUN_1800691a0(param_1);
  lVar2 = _UNK_1801acbd8;
  if (-1 < (int)uVar6) {
    uVar5 = *(uint *)((longlong)param_1 + 0x44);
    if (((100 < uVar5) || (uVar5 == 0)) ||
       ((uVar7 = 2, uVar5 != 100 &&
        ((uVar1 = *(uint *)(param_1 + 9), uVar1 <= uVar5 || 100 < uVar1 ||
         ((uVar7 = 3, uVar1 != 100 &&
          ((uVar5 = *(uint *)((longlong)param_1 + 0x4c), uVar5 <= uVar1 || 100 < uVar5 ||
           ((uVar7 = 4, uVar5 != 100 &&
            ((uVar1 = *(uint *)(param_1 + 10), uVar1 <= uVar5 || 100 < uVar1 ||
             ((uVar7 = 5, uVar1 != 100 &&
              ((uVar5 = *(uint *)((longlong)param_1 + 0x54), uVar5 <= uVar1 || 100 < uVar5 ||
               ((uVar7 = 6, uVar5 != 100 &&
                ((uVar1 = *(uint *)(param_1 + 0xb), uVar1 <= uVar5 || 100 < uVar1 ||
                 ((uVar7 = 7, uVar1 != 100 &&
                  ((uVar5 = *(uint *)((longlong)param_1 + 0x5c), uVar5 <= uVar1 || 100 < uVar5 ||
                   ((uVar7 = 8, uVar5 != 100 &&
                    ((uVar1 = *(uint *)(param_1 + 0xc), uVar1 <= uVar5 || 100 < uVar1 ||
                     ((uVar7 = 9, uVar1 != 100 &&
                      (uVar7 = 10, *(int *)((longlong)param_1 + 100) != 100 || 99 < uVar1)))))))))))
                 ))))))))))))))))))))) {
      param_1[8] = _DAT_1801acbd0;
      param_1[9] = lVar2;
      uVar4 = _UNK_1801acbec;
      uVar3 = _UNK_1801acbe8;
      uVar7 = _UNK_1801acbe4;
      *(undefined4 *)(param_1 + 0xd) = _DAT_1801acbe0;
      *(undefined4 *)((longlong)param_1 + 0x6c) = uVar7;
      *(undefined4 *)(param_1 + 0xe) = uVar3;
      *(undefined4 *)((longlong)param_1 + 0x74) = uVar4;
      uVar7 = 4;
    }
    *(undefined4 *)(param_1 + 0x3a) = uVar7;
    uVar5 = *(uint *)((longlong)param_1 + 0x9c);
    *(uint *)((longlong)param_1 + 0x14) = uVar5;
    if ((uVar5 & 0x1000) != 0) {
      FUN_18004cde0(param_2,1);
      uVar5 = *(uint *)((longlong)param_1 + 0x14);
    }
    *(uint *)(param_1 + 0x3d) = (uint)((uVar5 & 0x1000) == 0);
    (**(code **)(*param_1 + 0x48))(param_1,0,0,param_4,param_3,param_6);
    return uVar6 & 0xffffffff;
  }
  return uVar6;
}


```

## FUN_1800685a0 at `1800685a0`

```c

undefined8 FUN_1800685a0(longlong param_1,longlong param_2)

{
  int iVar1;
  uint uVar2;
  undefined8 uVar3;
  int iVar4;

  if (param_2 == 0) {
    uVar3 = 0x80004005;
  }
  else {
    uVar3 = 0;
    if (*(int *)(param_1 + 0x1e8) != 0) {
      if (*(int *)(param_1 + 0x1dc) == 0) {
        *(undefined4 *)(param_1 + 0x1e8) = 0;
        uVar3 = 0;
      }
      else {
        FUN_1800680f0();
        FUN_180068300(param_1);
        if (*(int *)(param_1 + 0xac) != 0) {
          iVar1 = *(int *)(param_1 + 0x1dc);
          iVar4 = 0;
          if (iVar1 == 1) {
            iVar4 = *(int *)(param_1 + 0x214);
          }
          uVar2 = (*(int *)(param_1 + 0x1d8) + 1U) % (uint)(iVar4 + *(int *)(param_1 + 0x1d4));
          *(uint *)(param_1 + 0x1d8) = uVar2;
          uVar3 = 0;
          if ((iVar1 != -1) && (uVar3 = 0, uVar2 == 0)) {
            *(int *)(param_1 + 0x1dc) = iVar1 + -1;
          }
        }
      }
    }
  }
  return uVar3;
}


```

## FUN_180068650 at `180068650`

```c

undefined8
FUN_180068650(longlong param_1,undefined8 param_2,undefined8 param_3,int param_4,int param_5)

{
  if ((((param_5 != 0) && ((*(uint *)(param_1 + 0x14) & 0x1000) != 0)) &&
      (*(int *)(param_1 + 0x224) != param_4)) && (*(int *)(param_1 + 0x224) = param_4, param_4 != 0)
     ) {
    if ((*(uint *)(param_1 + 0x14) & 4) != 0) {
      if (*(int *)(param_1 + 0x1e8) == 1) {
        *(undefined8 *)(param_1 + 0x1d8) = 0;
        *(int *)(param_1 + 0x1d4) = *(int *)(param_1 + 0x210) + *(int *)(param_1 + 0x20c);
        return 0;
      }
      *(undefined4 *)(param_1 + 0x1e8) = 1;
      *(undefined4 *)(param_1 + 0x1d8) = 0;
      *(undefined4 *)(param_1 + 0x1dc) = *(undefined4 *)(param_1 + 0x94);
      *(undefined4 *)(param_1 + 0x1e0) = *(undefined4 *)(param_1 + 0x94);
      *(int *)(param_1 + 0x1d4) = *(int *)(param_1 + 0x210) + *(int *)(param_1 + 0x20c);
      return 0;
    }
    if (*(int *)(param_1 + 0x1e8) != 1) {
      *(undefined4 *)(param_1 + 0x1d8) = 0;
      *(undefined4 *)(param_1 + 0x1dc) = *(undefined4 *)(param_1 + 0x94);
      *(undefined4 *)(param_1 + 0x1e0) = *(undefined4 *)(param_1 + 0x94);
      *(int *)(param_1 + 0x1d4) = *(int *)(param_1 + 0x210) + *(int *)(param_1 + 0x20c);
      FUN_180067f40();
      *(undefined4 *)(param_1 + 0x1e8) = 1;
    }
  }
  return 0;
}


```

## FUN_180068740 at `180068740`

```c

undefined8 FUN_180068740(longlong param_1)

{
  *(undefined4 *)(param_1 + 0x1d8) = 0;
  *(undefined4 *)(param_1 + 0x1dc) = *(undefined4 *)(param_1 + 0x94);
  *(undefined4 *)(param_1 + 0x1e0) = *(undefined4 *)(param_1 + 0x94);
  *(int *)(param_1 + 0x1d4) = *(int *)(param_1 + 0x210) + *(int *)(param_1 + 0x20c);
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

## FUN_1800675c0 at `1800675c0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_1800675c0(longlong param_1)

{
  uint uVar1;
  ulonglong uVar2;
  uint uVar3;
  uint uVar4;
  undefined4 uVar5;
  int iVar6;
  float fVar7;
  int iVar9;
  undefined1 auVar8 [16];
  int iVar10;
  int iVar11;
  undefined4 in_stack_00000028;

  uVar2 = FUN_180069290();
  if (-1 < (int)uVar2) {
    uVar3 = 10;
    if (10 < *(uint *)(param_1 + 0xa8)) {
      uVar3 = *(uint *)(param_1 + 0xa8);
    }
    *(uint *)(param_1 + 0xa8) = uVar3;
    *(undefined4 *)(param_1 + 0x1dc) = *(undefined4 *)(param_1 + 0x94);
    *(float *)(param_1 + 0x228) =
         (float)(((double)*(uint *)(param_1 + 0xb0) * DAT_1801acb08) / DAT_1801acbf0);
    uVar5 = FUN_180155ec0();
    *(undefined4 *)(param_1 + 0x230) = uVar5;
    uVar5 = FUN_18014de90();
    *(undefined4 *)(param_1 + 0x22c) = uVar5;
    FUN_180067830(param_1);
    auVar8._0_8_ = *(ulonglong *)(param_1 + 0x21c) & 0xffffffff;
    auVar8._8_4_ = (int)(*(ulonglong *)(param_1 + 0x21c) >> 0x20);
    auVar8._12_4_ = 0;
    iVar6 = (int)((float)*(undefined8 *)(param_1 + 0x22c) *
                 (float)(SUB168(auVar8 | _DAT_1801acc00,0) - (double)DAT_1801acc00));
    iVar9 = (int)((float)((ulonglong)*(undefined8 *)(param_1 + 0x22c) >> 0x20) *
                 (float)(SUB168(auVar8 | _DAT_1801acc00,8) - DAT_1801acc00._8_8_));
    iVar10 = iVar6 >> 0x1f;
    iVar11 = iVar9 >> 0x1f;
    uVar2 = CONCAT44(iVar9,iVar6) ^ CONCAT44(iVar11,iVar10);
    iVar6 = ((int)(uVar2 >> 0x20) - iVar11) + ((int)uVar2 - iVar10);
    uVar3 = iVar6 + (uint)(iVar6 == 0);
    *(uint *)(param_1 + 0x218) = uVar3;
    if ((*(byte *)(param_1 + 0x9d) & 0xc) != 0) {
      uVar3 = *(uint *)(param_1 + 0xa0);
      uVar1 = *(uint *)(param_1 + 0xa4);
      if (uVar1 == 0 || uVar3 == 0) {
        uVar3 = *(int *)(param_1 + 0x18) - 1;
        *(uint *)(param_1 + 0xa0) = uVar3;
        uVar1 = *(int *)(param_1 + 0x1c) - 1;
        *(uint *)(param_1 + 0xa4) = uVar1;
      }
      uVar3 = ((uVar1 & 0xffff) + ((uVar3 & 0xffff) - ((uVar1 >> 0x10) + (uVar3 >> 0x10)))) * 2;
      *(uint *)(param_1 + 0x218) = uVar3;
    }
    if (*(int *)(param_1 + 0xac) == 0) {
      uVar2 = (ulonglong)*(uint *)(param_1 + 0x38);
      fVar7 = DAT_1801a9690 / (float)uVar2;
    }
    else {
      uVar2 = (ulonglong)*(uint *)(param_1 + 0x38);
      fVar7 = (((float)(uint)(*(int *)(param_1 + 0xac) * 2) / DAT_1801a9a70) * (float)uVar3) /
              (float)uVar2;
    }
    *(float *)(param_1 + 0x1e4) = fVar7;
    *(int *)(param_1 + 0x23c) = (int)(longlong)((float)uVar3 / fVar7);
    uVar4 = (uint)(longlong)
                  ((double)((longlong)((float)uVar3 / fVar7) & 0xffffffff) *
                  ((double)*(uint *)(param_1 + 0xa8) / DAT_1801aca10));
    *(uint *)(param_1 + 0x20c) = uVar4;
    iVar6 = (int)((ulonglong)*(uint *)(param_1 + 0x98) / (1000 / uVar2));
    *(int *)(param_1 + 0x210) = iVar6;
    uVar1 = *(uint *)(param_1 + 0x1d0);
    if (uVar4 < uVar1) {
      *(uint *)(param_1 + 0x20c) = uVar1;
      uVar4 = uVar1;
    }
    *(uint *)(param_1 + 0x240) = (uint)(uVar3 < uVar1);
    *(uint *)(param_1 + 0x214) = uVar4;
    *(uint *)(param_1 + 0x208) = uVar4;
    *(uint *)(param_1 + 0x1d4) = iVar6 + uVar4;
    uVar3 = FUN_180067960(param_1,uVar3 < uVar1,1000 / uVar2,uVar3,in_stack_00000028);
    uVar2 = (ulonglong)uVar3;
    FUN_180067e50(param_1);
  }
  return uVar2;
}


```

## FUN_1800671b0 at `1800671b0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_1800671b0(undefined8 *param_1)

{
  undefined8 uVar1;

  FUN_180069140();
  *param_1 = CTidalEffect::vftable;
  param_1[0x30] = 0;
  param_1[0x31] = 0;
  param_1[0x32] = 0;
  param_1[0x34] = 0;
  param_1[0x35] = 0;
  *(undefined8 *)((longlong)param_1 + 0x1ab) = 0;
  *(undefined8 *)((longlong)param_1 + 0x1b3) = 0;
  *(undefined8 *)((longlong)param_1 + 0x1bc) = 0;
  *(undefined8 *)((longlong)param_1 + 0x1c1) = 0;
  param_1[0x3e] = 0;
  param_1[0x3f] = 0;
  param_1[0x40] = 0;
  param_1[0x41] = 0;
  *(undefined4 *)(param_1 + 0x46) = 0;
  param_1[0x45] = DAT_1801a9a60;
  *(undefined4 *)(param_1 + 0x42) = 0;
  uVar1 = _UNK_1801ac688;
  param_1[0x3a] = _DAT_1801ac680;
  param_1[0x3b] = uVar1;
  *(undefined4 *)(param_1 + 0x3d) = 1;
  *(undefined4 *)((longlong)param_1 + 0x224) = 0;
  return param_1;
}


```

## FUN_180067290 at `180067290`

```c

void FUN_180067290(undefined8 *param_1)

{
  ulonglong uVar1;
  longlong lVar2;
  longlong lVar3;

  *param_1 = CTidalEffect::vftable;
  if ((param_1[1] != 0) && ((*(byte *)((longlong)param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_1[1],0);
  }
  lVar2 = param_1[0x3e];
  lVar3 = param_1[0x3f];
  if (lVar2 != lVar3) {
    do {
      if (*(longlong *)(lVar2 + 0x18) != 0) {
        FUN_1800b9de0();
      }
      lVar2 = lVar2 + 0x20;
    } while (lVar2 != lVar3);
    lVar2 = param_1[0x3e];
    param_1[0x3f] = lVar2;
  }
  if (lVar2 != 0) {
    uVar1 = param_1[0x40] - lVar2;
    lVar3 = lVar2;
    if (0xfff < uVar1) {
      lVar3 = *(longlong *)(lVar2 + -8);
      if (0x1f < (ulonglong)((lVar2 + -8) - lVar3)) {
                    /* WARNING: Subroutine does not return */
        _invoke_watson((wchar_t *)0x0,(wchar_t *)0x0,(wchar_t *)0x0,0,0);
      }
      uVar1 = uVar1 + 0x27;
    }
    FUN_1800b9d98(lVar3,uVar1);
    param_1[0x3e] = 0;
    param_1[0x3f] = 0;
    param_1[0x40] = 0;
  }
  FUN_180069190(param_1);
  return;
}


```

## FUN_180067830 at `180067830`

```c

void FUN_180067830(longlong param_1)

{
  int iVar1;
  longlong lVar2;
  uint uVar3;
  uint uVar4;
  uint uVar5;
  uint uVar6;
  uint uVar7;
  double dVar8;

  uVar5 = *(uint *)(param_1 + 0x1c);
  *(uint *)(param_1 + 0x21c) = uVar5;
  *(undefined4 *)(param_1 + 0x220) = *(undefined4 *)(param_1 + 0x18);
  if ((*(uint *)(param_1 + 0xb4) < 0x100) && (*(uint *)(param_1 + 0xb8) < 0x100)) {
    lVar2 = *(longlong *)(param_1 + 8);
    iVar1 = *(int *)(lVar2 + 0xe0);
    uVar4 = (*(uint *)(param_1 + 0xb8) * 0x28) / *(uint *)(lVar2 + 0xf0);
    uVar6 = uVar4 - *(int *)(lVar2 + 0xdc);
    *(uint *)(param_1 + 0x234) = uVar4;
    uVar3 = *(uint *)(param_1 + 0xb4) * 0x28;
    uVar7 = *(uint *)(lVar2 + 0xec);
    *(uint *)(param_1 + 0x234) = uVar6;
    uVar4 = uVar3 / uVar7 - iVar1;
    *(uint *)(param_1 + 0x238) = uVar4;
    if ((int)uVar4 < 0) {
      dVar8 = (double)FUN_1800680e0(uVar4,(ulonglong)uVar3 % (ulonglong)uVar7);
      *(int *)(param_1 + 0x21c) = (int)(longlong)((double)*(uint *)(param_1 + 0x1c) + dVar8);
      uVar6 = *(uint *)(param_1 + 0x234);
      goto joined_r0x000180067917;
    }
  }
  else {
    uVar6 = *(uint *)(param_1 + 0x30) >> 1;
    *(uint *)(param_1 + 0x234) = uVar6;
    uVar4 = *(uint *)(param_1 + 0x34) >> 1;
    *(uint *)(param_1 + 0x238) = uVar4;
  }
  if (uVar5 < uVar4) {
    *(uint *)(param_1 + 0x21c) = uVar4;
  }
  else {
    uVar7 = uVar5 - uVar4;
    if ((int)(uVar5 - uVar4) < (int)uVar4) {
      uVar7 = uVar4;
    }
    *(uint *)(param_1 + 0x21c) = uVar7;
  }
joined_r0x000180067917:
  if ((int)uVar6 < 0) {
    dVar8 = (double)FUN_1800680e0();
    uVar6 = (uint)(longlong)((double)*(uint *)(param_1 + 0x18) + dVar8);
  }
  else {
    uVar5 = *(uint *)(param_1 + 0x18) - uVar6;
    if (uVar6 <= *(uint *)(param_1 + 0x18)) {
      if ((int)uVar5 < (int)uVar6) {
        uVar5 = uVar6;
      }
      *(uint *)(param_1 + 0x220) = uVar5;
      return;
    }
  }
  *(uint *)(param_1 + 0x220) = uVar6;
  return;
}


```

## FUN_180067960 at `180067960`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 FUN_180067960(longlong param_1)

{
  longlong lVar1;
  longlong lVar2;
  undefined8 *puVar3;
  void *pvVar4;
  longlong lVar5;
  __uint64 _Var6;
  ulonglong uVar7;
  uint uVar8;
  undefined4 local_70;
  undefined4 uStack_6c;
  double dStack_68;
  bool local_60;
  void *local_58;
  undefined4 local_4c;
  undefined8 local_48;

  local_48 = 0xfffffffffffffffe;
  lVar5 = *(longlong *)(param_1 + 0x1f0);
  lVar2 = *(longlong *)(param_1 + 0x1f8);
  if (lVar5 != lVar2) {
    do {
      if (*(longlong *)(lVar5 + 0x18) != 0) {
        FUN_1800b9de0();
      }
      lVar5 = lVar5 + 0x20;
    } while (lVar5 != lVar2);
    *(undefined8 *)(param_1 + 0x1f8) = *(undefined8 *)(param_1 + 0x1f0);
  }
  local_60 = false;
  local_58 = (void *)0x0;
  local_70 = 1;
  dStack_68 = 0.0;
  _Var6 = (ulonglong)*(uint *)(param_1 + 0x208) << 2;
  pvVar4 = operator_new(_Var6);
  FUN_180194db0(pvVar4,0,_Var6);
  uVar8 = *(uint *)(param_1 + 0x14);
  if (((uVar8 & 0x1000) == 0) || (*(int *)(param_1 + 0x94) == 0)) {
    local_60 = (uVar8 & 0x1000) == 0;
    if (local_60) goto LAB_180067a2c;
  }
  else {
    local_60 = true;
LAB_180067a2c:
    *(int *)(param_1 + 0x1e0) = *(int *)(param_1 + 0x1e0) + -1;
  }
  lVar5 = param_1 + 0x68;
  lVar2 = param_1 + 0x40;
  local_58 = pvVar4;
  if ((uVar8 & 1) == 0) {
LAB_180067a93:
    FUN_180069590(param_1,0x64000000,*(undefined4 *)(param_1 + 0x208),local_58);
    FUN_1800692e0(param_1,10,lVar2,lVar5,*(undefined4 *)(param_1 + 0x20c),local_58);
  }
  else {
    for (uVar8 = 0; uVar8 < *(uint *)(param_1 + 0x1d0); uVar8 = uVar8 + 1) {
      uVar7 = (ulonglong)uVar8;
      if (((*(char *)(lVar5 + uVar7 * 4) != '\0') || (*(char *)(param_1 + 0x69 + uVar7 * 4) != '\0')
          ) || (*(char *)(param_1 + 0x6a + uVar7 * 4) != '\0')) {
        FUN_180069600(param_1,&local_4c);
        *(undefined4 *)(lVar5 + uVar7 * 4) = local_4c;
      }
    }
    if (local_58 != (void *)0x0) goto LAB_180067a93;
  }
  pvVar4 = local_58;
  lVar1 = param_1 + 0x1f0;
  puVar3 = *(undefined8 **)(param_1 + 0x1f8);
  if (puVar3 == *(undefined8 **)(param_1 + 0x200)) {
    FUN_180068800(lVar1,puVar3,&local_70);
  }
  else {
    *(bool *)(puVar3 + 2) = local_60;
    *puVar3 = CONCAT44(uStack_6c,local_70);
    puVar3[1] = dStack_68;
    local_58 = (void *)0x0;
    puVar3[3] = pvVar4;
    *(longlong *)(param_1 + 0x1f8) = *(longlong *)(param_1 + 0x1f8) + 0x20;
  }
  if (local_58 != (void *)0x0) {
    FUN_1800b9de0();
  }
  local_60 = false;
  local_58 = (void *)0x0;
  local_70 = 2;
  dStack_68 = (double)((ulonglong)(double)*(uint *)(param_1 + 0x208) | DAT_18019d6c0);
  _Var6 = (ulonglong)*(uint *)(param_1 + 0x208) << 2;
  pvVar4 = operator_new(_Var6);
  FUN_180194db0(pvVar4,0,_Var6);
  uVar8 = *(uint *)(param_1 + 0x14);
  if (((uVar8 & 0x1000) == 0) || (*(uint *)(param_1 + 0x94) < 2)) {
    local_60 = (uVar8 & 0x1000) == 0;
    if (local_60) goto LAB_180067b9c;
  }
  else {
    local_60 = true;
LAB_180067b9c:
    *(int *)(param_1 + 0x1e0) = *(int *)(param_1 + 0x1e0) + -1;
  }
  local_58 = pvVar4;
  if ((uVar8 & 1) == 0) {
LAB_180067bf3:
    FUN_180069590(param_1,0x64000000,*(undefined4 *)(param_1 + 0x208),local_58);
    FUN_1800692e0(param_1,10,lVar2,lVar5,*(undefined4 *)(param_1 + 0x20c),local_58);
  }
  else {
    for (uVar8 = 0; uVar8 < *(uint *)(param_1 + 0x1d0); uVar8 = uVar8 + 1) {
      uVar7 = (ulonglong)uVar8;
      if (((*(char *)(lVar5 + uVar7 * 4) != '\0') || (*(char *)(param_1 + 0x69 + uVar7 * 4) != '\0')
          ) || (*(char *)(param_1 + 0x6a + uVar7 * 4) != '\0')) {
        FUN_180069600(param_1,&local_4c);
        *(undefined4 *)(lVar5 + uVar7 * 4) = local_4c;
      }
    }
    if (local_58 != (void *)0x0) goto LAB_180067bf3;
  }
  pvVar4 = local_58;
  puVar3 = *(undefined8 **)(param_1 + 0x1f8);
  if (puVar3 == *(undefined8 **)(param_1 + 0x200)) {
    FUN_180068800(lVar1,puVar3,&local_70);
  }
  else {
    *(bool *)(puVar3 + 2) = local_60;
    *puVar3 = CONCAT44(uStack_6c,local_70);
    puVar3[1] = dStack_68;
    local_58 = (void *)0x0;
    puVar3[3] = pvVar4;
    *(longlong *)(param_1 + 0x1f8) = *(longlong *)(param_1 + 0x1f8) + 0x20;
  }
  if (local_58 != (void *)0x0) {
    FUN_1800b9de0();
  }
  local_60 = false;
  local_58 = (void *)0x0;
  local_70 = 3;
  dStack_68 = (double)*(uint *)(param_1 + 0x208) * _DAT_1801acc10;
  _Var6 = (ulonglong)*(uint *)(param_1 + 0x208) << 2;
  pvVar4 = operator_new(_Var6);
  FUN_180194db0(pvVar4,0,_Var6);
  uVar8 = *(uint *)(param_1 + 0x14);
  if (((uVar8 & 0x1000) == 0) || (*(uint *)(param_1 + 0x94) < 3)) {
    local_60 = (uVar8 & 0x1000) == 0;
    if (local_60) goto LAB_180067cf7;
  }
  else {
    local_60 = true;
LAB_180067cf7:
    *(int *)(param_1 + 0x1e0) = *(int *)(param_1 + 0x1e0) + -1;
  }
  local_58 = pvVar4;
  if ((uVar8 & 1) != 0) {
    for (uVar8 = 0; uVar8 < *(uint *)(param_1 + 0x1d0); uVar8 = uVar8 + 1) {
      uVar7 = (ulonglong)uVar8;
      if (((*(char *)(lVar5 + uVar7 * 4) != '\0') || (*(char *)(param_1 + 0x69 + uVar7 * 4) != '\0')
          ) || (*(char *)(param_1 + 0x6a + uVar7 * 4) != '\0')) {
        FUN_180069600(param_1,&local_4c);
        *(undefined4 *)(lVar5 + uVar7 * 4) = local_4c;
      }
    }
    if (local_58 == (void *)0x0) goto LAB_180067d9a;
  }
  FUN_180069590(param_1,0x64000000,*(undefined4 *)(param_1 + 0x208),local_58);
  FUN_1800692e0(param_1,10,lVar2,lVar5,*(undefined4 *)(param_1 + 0x20c),local_58);
LAB_180067d9a:
  pvVar4 = local_58;
  puVar3 = *(undefined8 **)(param_1 + 0x1f8);
  if (puVar3 == *(undefined8 **)(param_1 + 0x200)) {
    FUN_180068800(lVar1,puVar3,&local_70);
  }
  else {
    *(bool *)(puVar3 + 2) = local_60;
    *puVar3 = CONCAT44(uStack_6c,local_70);
    puVar3[1] = dStack_68;
    local_58 = (void *)0x0;
    puVar3[3] = pvVar4;
    *(longlong *)(param_1 + 0x1f8) = *(longlong *)(param_1 + 0x1f8) + 0x20;
  }
  if (local_58 != (void *)0x0) {
    FUN_1800b9de0();
  }
  return 0;
}


```

## FUN_180067e50 at `180067e50`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

void FUN_180067e50(longlong param_1)

{
  undefined8 uVar1;
  undefined1 auVar2 [16];
  undefined1 auVar3 [16];
  undefined1 auVar4 [16];
  ulonglong uVar5;
  undefined1 auVar6 [16];
  undefined1 auVar7 [16];

  auVar2._8_8_ = 0;
  auVar2._0_8_ = *(ulonglong *)(param_1 + 0x21c);
  uVar1 = *(undefined8 *)(param_1 + 0x18);
  auVar3._4_4_ = (int)uVar1;
  auVar3._0_4_ = (int)((ulonglong)uVar1 >> 0x20);
  auVar3._8_8_ = 0;
  auVar6 = auVar2 ^ _DAT_1801abf90;
  auVar4 = _DAT_1801abf90 ^ auVar3;
  auVar7._0_4_ = -(uint)(auVar4._0_4_ < auVar6._0_4_);
  auVar7._4_4_ = -(uint)(auVar4._4_4_ < auVar6._4_4_);
  auVar7._8_4_ = -(uint)(auVar4._8_4_ < auVar6._8_4_);
  auVar7._12_4_ = -(uint)(auVar4._12_4_ < auVar6._12_4_);
  auVar2 = ~auVar7 & auVar3 | auVar2 & auVar7;
  uVar5 = auVar2._0_8_;
  *(ulonglong *)(param_1 + 0x1b0) = uVar5;
  auVar4._0_8_ = uVar5 & 0xffffffff;
  auVar4._8_4_ = auVar2._4_4_;
  auVar4._12_4_ = 0;
  auVar6._0_8_ = SUB168(auVar4 | _DAT_1801acc00,0) - (double)DAT_1801acc00;
  auVar6._8_8_ = SUB168(auVar4 | _DAT_1801acc00,8) - DAT_1801acc00._8_8_;
  auVar2 = divpd(_DAT_1801acc20,auVar6);
  *(undefined1 (*) [16])(param_1 + 0x180) = auVar2;
  *(double *)(param_1 + 400) =
       (double)((float)*(uint *)(param_1 + 0x23c) * (float)*(undefined8 *)(param_1 + 0x22c));
  *(double *)(param_1 + 0x198) =
       (double)((float)*(uint *)(param_1 + 0x23c) *
               (float)((ulonglong)*(undefined8 *)(param_1 + 0x22c) >> 0x20));
  *(double *)(param_1 + 0x1a0) =
       (double)(int)((ulonglong)*(undefined8 *)(param_1 + 0x234) >> 0x20) * auVar2._0_8_;
  *(double *)(param_1 + 0x1a8) = (double)(int)*(undefined8 *)(param_1 + 0x234) * auVar2._8_8_;
  *(bool *)(param_1 + 0x1b8) = *(int *)(param_1 + 0xbc) == 0;
  *(bool *)(param_1 + 0x1b9) = *(int *)(param_1 + 0xc0) == 100;
  *(char *)(param_1 + 0x1ba) = (char)*(int *)(param_1 + 0xc0);
  *(undefined8 *)(param_1 + 0x1bc) = uVar1;
  *(undefined4 *)(param_1 + 0x1c4) = 0;
  *(undefined1 *)(param_1 + 0x1c8) = 1;
  return;
}


```

## FUN_180067f40 at `180067f40`

```c

void FUN_180067f40(longlong param_1)

{
  longlong lVar1;
  uint uVar2;
  ulonglong uVar3;
  uint uVar4;
  longlong lVar5;
  bool bVar6;
  undefined1 auStack_88 [32];
  undefined4 local_68;
  undefined8 local_60;
  undefined4 local_4c;
  ulonglong local_48;

  local_48 = DAT_1801f4b40 ^ (ulonglong)auStack_88;
  lVar5 = *(longlong *)(param_1 + 0x1f0);
  lVar1 = *(longlong *)(param_1 + 0x1f8);
  if (lVar5 != lVar1) {
    uVar4 = 0;
    do {
      *(double *)(lVar5 + 8) = (double)*(uint *)(param_1 + 0x208) * (double)(int)-uVar4;
      if (((*(uint *)(param_1 + 0x14) & 0x1000) == 0) || (*(uint *)(param_1 + 0x94) <= uVar4)) {
        bVar6 = (*(uint *)(param_1 + 0x14) & 0x1000) == 0;
        *(bool *)(lVar5 + 0x10) = bVar6;
        if (bVar6) goto LAB_180067fec;
      }
      else {
        *(undefined1 *)(lVar5 + 0x10) = 1;
LAB_180067fec:
        if (*(int *)(param_1 + 0x1e0) != 0) {
          *(int *)(param_1 + 0x1e0) = *(int *)(param_1 + 0x1e0) + -1;
        }
      }
      uVar2 = *(uint *)(param_1 + 0x1d0);
      if ((uVar2 != 0 & *(byte *)(param_1 + 0x14)) == 1) {
        uVar3 = 0;
        do {
          if (((*(char *)(param_1 + 0x68 + uVar3 * 4) != '\0') ||
              (*(char *)(param_1 + 0x69 + uVar3 * 4) != '\0')) ||
             (*(char *)(param_1 + 0x6a + uVar3 * 4) != '\0')) {
            FUN_180069600(param_1,&local_4c);
            *(undefined4 *)(param_1 + 0x68 + uVar3 * 4) = local_4c;
            uVar2 = *(uint *)(param_1 + 0x1d0);
          }
          uVar3 = uVar3 + 1;
        } while (uVar3 < uVar2);
      }
      if (*(longlong *)(lVar5 + 0x18) != 0) {
        FUN_180069590(param_1,0x64000000,*(undefined4 *)(param_1 + 0x208));
        local_60 = *(undefined8 *)(lVar5 + 0x18);
        local_68 = *(undefined4 *)(param_1 + 0x20c);
        FUN_1800692e0(param_1,10,param_1 + 0x40,param_1 + 0x68);
      }
      uVar4 = uVar4 + 1;
      lVar5 = lVar5 + 0x20;
    } while (lVar5 != lVar1);
  }
  if ((local_48 ^ (ulonglong)auStack_88) != DAT_1801f4b40) {
                    /* WARNING: Subroutine does not return */
    FUN_1800b9f70();
  }
  return;
}


```

## FUN_1800680f0 at `1800680f0`

```c

void FUN_1800680f0(longlong param_1)

{
  longlong lVar1;
  longlong lVar2;
  double dVar3;
  longlong lVar4;
  uint uVar5;
  longlong lVar6;
  longlong lVar7;
  ulonglong uVar8;
  longlong lVar9;
  bool bVar10;
  bool bVar11;
  double dVar12;
  undefined1 auStack_88 [32];
  undefined4 local_68;
  longlong local_60;
  undefined4 local_54;
  ulonglong local_50;

  dVar3 = DAT_1801aadd8;
  local_50 = DAT_1801f4b40 ^ (ulonglong)auStack_88;
  lVar9 = *(longlong *)(param_1 + 0x1f0);
  lVar1 = *(longlong *)(param_1 + 0x1f8);
  if (lVar9 != lVar1) {
    do {
      dVar12 = *(double *)(lVar9 + 8) + dVar3;
      *(double *)(lVar9 + 8) = dVar12;
      if ((double)*(int *)(param_1 + 0x23c) < dVar12 - (double)(int)*(uint *)(param_1 + 0x20c)) {
        lVar7 = *(longlong *)(param_1 + 0x1f0);
        lVar2 = *(longlong *)(param_1 + 0x1f8);
        lVar6 = lVar7 + 0x20;
        if (lVar6 != lVar2 && lVar7 != lVar2) {
          do {
            lVar4 = lVar6;
            if (*(double *)(lVar7 + 8) < *(double *)(lVar6 + 8) ||
                *(double *)(lVar7 + 8) == *(double *)(lVar6 + 8)) {
              lVar4 = lVar7;
            }
            lVar7 = lVar4;
            lVar6 = lVar6 + 0x20;
          } while (lVar6 != lVar2);
        }
        *(double *)(lVar9 + 8) = *(double *)(lVar7 + 8) - (double)*(uint *)(param_1 + 0x20c);
        bVar10 = (*(uint *)(param_1 + 0x14) & 0x1000) == 0;
        bVar11 = *(int *)(param_1 + 0x1e0) != 0;
        *(bool *)(lVar9 + 0x10) = bVar11 || bVar10;
        if ((bVar11 || bVar10) && (*(int *)(param_1 + 0x1e0) != 0)) {
          *(int *)(param_1 + 0x1e0) = *(int *)(param_1 + 0x1e0) + -1;
        }
        if ((*(byte *)(param_1 + 0x14) & 1) != 0) {
          uVar5 = *(uint *)(param_1 + 0x1d0);
          if (uVar5 != 0) {
            uVar8 = 0;
            do {
              if (((*(char *)(param_1 + 0x68 + uVar8 * 4) != '\0') ||
                  (*(char *)(param_1 + 0x69 + uVar8 * 4) != '\0')) ||
                 (*(char *)(param_1 + 0x6a + uVar8 * 4) != '\0')) {
                FUN_180069600(param_1,&local_54);
                *(undefined4 *)(param_1 + 0x68 + uVar8 * 4) = local_54;
                uVar5 = *(uint *)(param_1 + 0x1d0);
              }
              uVar8 = uVar8 + 1;
            } while (uVar8 < uVar5);
          }
          if (*(longlong *)(lVar9 + 0x18) != 0) {
            local_68 = *(undefined4 *)(param_1 + 0x20c);
            local_60 = *(longlong *)(lVar9 + 0x18);
            FUN_1800692e0(param_1,10,param_1 + 0x40,param_1 + 0x68);
          }
        }
      }
      lVar9 = lVar9 + 0x20;
    } while (lVar9 != lVar1);
  }
  if ((local_50 ^ (ulonglong)auStack_88) != DAT_1801f4b40) {
                    /* WARNING: Subroutine does not return */
    FUN_1800b9f70();
  }
  return;
}


```

## FUN_180068300 at `180068300`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 FUN_180068300(longlong param_1)

{
  char *pcVar1;
  longlong lVar2;
  double dVar3;
  double dVar4;
  undefined1 uVar5;
  uint uVar6;
  int iVar7;
  int iVar8;
  double dVar9;
  ulonglong uVar10;
  double dVar11;
  uint uVar12;
  longlong lVar13;
  uint uVar14;
  longlong lVar15;
  longlong lVar16;
  ulonglong uVar17;
  ulonglong uVar18;
  longlong lVar19;
  double dVar20;
  double dVar21;
  double dVar22;
  longlong in_stack_00000038;

  if (*(char *)(param_1 + 0x1c8) == '\0') {
    FUN_180067e50(param_1);
  }
  dVar11 = DAT_1801acc30;
  uVar10 = _DAT_1801a93f0;
  uVar14 = *(uint *)(param_1 + 0x1bc);
  if ((uVar14 != 0) && (*(int *)(param_1 + 0x1c0) != 0)) {
    uVar6 = *(uint *)(param_1 + 0x23c);
    iVar7 = *(int *)(param_1 + 0x20c);
    lVar16 = -1;
    uVar12 = 0;
    uVar18 = 1;
    do {
      while ((int)uVar18 != 0) {
        iVar8 = *(int *)(param_1 + 0x234);
        dVar3 = *(double *)(param_1 + 0x188);
        lVar2 = in_stack_00000038 +
                (ulonglong)(*(int *)(param_1 + 0x1c) * uVar12 + *(int *)(param_1 + 0x1c4)) * 4;
        dVar4 = *(double *)(param_1 + 0x198);
        uVar17 = 0;
        do {
          dVar21 = (double)((ulonglong)
                            ((double)((int)uVar17 - *(int *)(param_1 + 0x238)) *
                             *(double *)(param_1 + 0x180) * *(double *)(param_1 + 400) +
                            (double)(int)(uVar12 - iVar8) * dVar3 * dVar4) & uVar10);
          dVar20 = (double)uVar6 - dVar21;
          dVar22 = dVar20;
          if (*(char *)(param_1 + 0x1b8) == '\0') {
            dVar22 = dVar21;
          }
          lVar15 = *(longlong *)(param_1 + 0x1f0);
          if (lVar16 < 0) {
            lVar13 = *(longlong *)(param_1 + 0x1f8);
            for (lVar19 = lVar15; (lVar19 != lVar13 && (*(char *)(lVar19 + 0x10) == '\0'));
                lVar19 = lVar19 + 0x20) {
            }
            lVar16 = lVar19 - lVar15 >> 5;
            if (lVar19 != lVar13) goto LAB_1800684a2;
          }
          else {
            lVar19 = lVar15 + lVar16 * 0x20;
            lVar13 = *(longlong *)(param_1 + 0x1f8);
            if (lVar19 != lVar13) {
LAB_1800684a2:
              do {
                dVar21 = *(double *)(lVar19 + 8);
                lVar15 = lVar19;
                if ((dVar22 < dVar21) &&
                   (dVar9 = dVar21 - (double)*(uint *)(param_1 + 0x20c), dVar9 <= dVar22)) {
                  dVar21 = (dVar21 - dVar22) + dVar11;
                  if (*(char *)(param_1 + 0x1b8) != '\0') {
                    dVar21 = dVar20 - dVar9;
                  }
                  lVar15 = *(longlong *)(lVar19 + 0x18);
                  if (((lVar15 != 0) && (uVar14 = (uint)dVar21, -1 < (int)uVar14)) &&
                     ((int)uVar14 < iVar7)) {
                    uVar18 = (ulonglong)uVar14;
                    *(undefined4 *)(lVar2 + uVar17 * 4) = *(undefined4 *)(lVar15 + uVar18 * 4);
                    if ((*(char *)(param_1 + 0x1b9) != '\x01') ||
                       (*(char *)(lVar15 + 2 + uVar18 * 4) != '\0' ||
                        (uint)*(byte *)(lVar15 + 1 + uVar18 * 4) +
                        (uint)*(byte *)(lVar15 + uVar18 * 4) != 0)) goto LAB_18006855a;
                  }
                  break;
                }
                do {
                  lVar19 = lVar15 + 0x20;
                  if (lVar19 == lVar13) goto LAB_180068540;
                  pcVar1 = (char *)(lVar15 + 0x30);
                  lVar15 = lVar19;
                } while (*pcVar1 == '\0');
              } while (lVar19 != lVar13);
            }
          }
LAB_180068540:
          uVar5 = *(undefined1 *)(param_1 + 0x1ba);
          *(undefined2 *)(lVar2 + uVar17 * 4) = 0;
          *(undefined1 *)(lVar2 + 2 + uVar17 * 4) = 0;
          *(undefined1 *)(lVar2 + 3 + uVar17 * 4) = uVar5;
LAB_18006855a:
          uVar17 = uVar17 + 1;
          uVar18 = (ulonglong)*(uint *)(param_1 + 0x1c0);
        } while (uVar17 < uVar18);
        uVar14 = *(uint *)(param_1 + 0x1bc);
        uVar12 = uVar12 + 1;
        if (uVar14 <= uVar12) {
          return 0;
        }
      }
      uVar18 = 0;
      uVar12 = uVar12 + 1;
    } while (uVar12 < uVar14);
  }
  return 0;
}


```
