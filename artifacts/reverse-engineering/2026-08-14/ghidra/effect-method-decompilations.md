# RzLightingEngineApi_v4.0.55.0.dll

Image base: `180000000`

## Matches
- `symbol` `fun_180053690` at `180053690`: `FUN_180053690`
- `symbol` `fun_180053a20` at `180053a20`: `FUN_180053a20`
- `symbol` `fun_1800541d0` at `1800541d0`: `FUN_1800541d0`
- `symbol` `fun_1800603b0` at `1800603b0`: `FUN_1800603b0`
- `symbol` `fun_180060790` at `180060790`: `FUN_180060790`
- `symbol` `fun_180061e90` at `180061e90`: `FUN_180061e90`
- `symbol` `fun_180062130` at `180062130`: `FUN_180062130`
- `symbol` `fun_1800621d0` at `1800621d0`: `FUN_1800621d0`
- `symbol` `fun_1800624a0` at `1800624a0`: `FUN_1800624a0`
- `symbol` `fun_1800625d0` at `1800625d0`: `FUN_1800625d0`
- `symbol` `fun_1800633b0` at `1800633b0`: `FUN_1800633b0`
- `symbol` `fun_180063890` at `180063890`: `FUN_180063890`
- `symbol` `fun_180063b30` at `180063b30`: `FUN_180063b30`
- `symbol` `fun_180063e30` at `180063e30`: `FUN_180063e30`
- `symbol` `fun_1800651c0` at `1800651c0`: `FUN_1800651c0`
- `symbol` `fun_180065570` at `180065570`: `FUN_180065570`
- `symbol` `fun_180065730` at `180065730`: `FUN_180065730`
- `symbol` `fun_180065a80` at `180065a80`: `FUN_180065a80`
- `symbol` `fun_180065c90` at `180065c90`: `FUN_180065c90`
- `symbol` `fun_1800666b0` at `1800666b0`: `FUN_1800666b0`
- `symbol` `fun_1800667d0` at `1800667d0`: `FUN_1800667d0`

## FUN_180053690 at `180053690`

Callers:
- none resolved

```c

int FUN_180053690(longlong param_1,longlong param_2,uint param_3,uint param_4,uint param_5)

{
  int iVar1;
  uint uVar2;
  int iVar3;
  longlong lVar4;
  uint uVar5;
  uint uVar6;
  bool bVar7;
  
  iVar1 = FUN_1800691a0();
  if (iVar1 < 0) {
    return iVar1;
  }
  uVar6 = *(uint *)(param_1 + 0x9c);
  *(uint *)(param_1 + 0x14) = uVar6;
  if ((uVar6 & 0x1000) == 0) {
    *(undefined4 *)(param_1 + 400) = *(undefined4 *)(param_1 + 0x94);
    uVar2 = 1;
  }
  else {
    *(undefined4 *)(param_1 + 400) = 0xffffffff;
    uVar2 = (uint)((uVar6 & 4) == 0);
  }
  *(uint *)(param_1 + 0x194) = uVar2;
  *(undefined4 *)(param_1 + 0x40) = 0;
  *(undefined4 *)(param_1 + 100) = 100;
  uVar2 = *(uint *)(param_1 + 0x44);
  bVar7 = true;
  iVar3 = 0;
  if (uVar2 < 0x65) {
    if (uVar2 == 0) {
      iVar3 = 0;
    }
    else {
      if (uVar2 == 100) {
        iVar3 = 2;
      }
      else {
        uVar5 = *(uint *)(param_1 + 0x48);
        if (uVar5 <= uVar2 || 100 < uVar5) goto LAB_18005386e;
        iVar3 = 0;
        if (uVar5 == 100) {
          iVar3 = 3;
        }
        else {
          uVar2 = *(uint *)(param_1 + 0x4c);
          if (uVar2 <= uVar5 || 100 < uVar2) goto LAB_18005386e;
          iVar3 = 0;
          if (uVar2 == 100) {
            iVar3 = 4;
          }
          else {
            uVar5 = *(uint *)(param_1 + 0x50);
            if (uVar5 <= uVar2 || 100 < uVar5) goto LAB_18005386e;
            iVar3 = 0;
            if (uVar5 == 100) {
              iVar3 = 5;
            }
            else {
              uVar2 = *(uint *)(param_1 + 0x54);
              if (uVar2 <= uVar5 || 100 < uVar2) goto LAB_18005386e;
              iVar3 = 0;
              if (uVar2 == 100) {
                iVar3 = 6;
              }
              else {
                uVar5 = *(uint *)(param_1 + 0x58);
                if (uVar5 <= uVar2 || 100 < uVar5) goto LAB_18005386e;
                iVar3 = 0;
                if (uVar5 == 100) {
                  iVar3 = 7;
                }
                else {
                  uVar2 = *(uint *)(param_1 + 0x5c);
                  if (uVar2 <= uVar5 || 100 < uVar2) goto LAB_18005386e;
                  iVar3 = 0;
                  if (uVar2 == 100) {
                    iVar3 = 8;
                  }
                  else {
                    uVar5 = *(uint *)(param_1 + 0x60);
                    if (uVar5 <= uVar2 || 100 < uVar5) goto LAB_18005386e;
                    iVar3 = (uVar5 != 100) + 9;
                  }
                }
              }
            }
          }
        }
      }
      bVar7 = false;
    }
  }
LAB_18005386e:
  *(int *)(param_1 + 0x180) = iVar3;
  if ((((uVar6 & 0x1000) == 0) && (*(int *)(param_1 + 0xa0) == 0)) &&
     (*(int *)(param_1 + 0xa4) == 0)) {
    *(uint *)(param_1 + 0xa0) = param_3 >> 1;
    *(uint *)(param_1 + 0xa4) = param_4 >> 1;
  }
  uVar2 = param_3 >> 1;
  if (param_3 >> 1 <= param_4 >> 1) {
    uVar2 = param_4 >> 1;
  }
  uVar5 = *(uint *)(param_1 + 0xac);
  if (0x19 < uVar5) {
    if ((float)((ulonglong)uVar5 / (ulonglong)param_5) <= (float)uVar2 + DAT_1801aadd0)
    goto LAB_180053907;
    uVar5 = (uint)(longlong)(((float)uVar2 + DAT_1801aadd0) * (float)param_5);
    *(uint *)(param_1 + 0xac) = uVar5;
  }
  if (uVar5 == 0) {
    *(undefined4 *)(param_1 + 0xac) = 1;
    uVar5 = 1;
  }
LAB_180053907:
  *(uint *)(param_1 + 0x184) = uVar5;
  uVar2 = param_4;
  if (param_4 < param_3) {
    uVar2 = param_3;
  }
  *(float *)(param_1 + 0x198) = (float)uVar5 / (float)param_5;
  if (param_4 < param_3) {
    param_3 = param_4;
  }
  *(int *)(param_1 + 0x188) =
       (int)(longlong)
            ((float)(*(uint *)(param_1 + 0xa8) / 100 + (param_3 >> 1) + uVar2 + 1) /
             ((float)uVar5 / (float)param_5) + DAT_1801a9690);
  if (bVar7) {
    *(undefined8 *)(param_1 + 0x40) = 0x6400000000;
    *(undefined4 *)(param_1 + 0x68) = 0xff;
    *(undefined2 *)(param_1 + 0x6c) = 0;
    uVar6 = uVar6 | 0x101;
    *(uint *)(param_1 + 0x9c) = uVar6;
  }
  if ((uVar6 & 0x19) != 0) {
    *(undefined8 *)(param_1 + 0x40) = 0x6400000000;
    *(undefined2 *)(param_1 + 0x68) = 0xffff;
    *(undefined1 *)(param_1 + 0x6a) = 0xff;
    *(undefined2 *)(param_1 + 0x6c) = 0;
    *(undefined1 *)(param_1 + 0x6e) = 0;
  }
  lVar4 = FUN_18014bdf0(0);
  *(ulonglong *)(param_1 + 0x178) = (lVar4 / 1800000) * 1800000 | 0x1f;
  if ((param_2 != 0) && ((*(byte *)(param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_2,1);
  }
  return iVar1;
}

```

## FUN_180053a20 at `180053a20`

Callers:
- none resolved

```c

undefined8 FUN_180053a20(longlong param_1,longlong param_2)

{
  longlong lVar1;
  uint uVar2;
  uint uVar3;
  undefined8 uVar4;
  longlong lVar5;
  longlong *plVar6;
  uint uVar7;
  ulonglong uVar8;
  longlong *plVar9;
  ulonglong uVar10;
  
  if (param_2 == 0) {
    uVar4 = 0x80004005;
  }
  else {
    uVar4 = 0;
    if (*(int *)(param_1 + 0x194) != 0) {
      if (*(int *)(param_1 + 400) == 0) {
        *(undefined4 *)(param_1 + 0x194) = 0;
        uVar4 = 0;
      }
      else {
        plVar6 = *(longlong **)(param_1 + 0x1a0);
        plVar9 = (longlong *)*plVar6;
        if (plVar9 != plVar6) {
          do {
            FUN_180053c60(plVar9[2]);
            plVar9 = (longlong *)*plVar9;
            plVar6 = *(longlong **)(param_1 + 0x1a0);
          } while (plVar9 != plVar6);
        }
        uVar7 = (*(int *)(param_1 + 0x18c) + 1U) % *(uint *)(param_1 + 0x188);
        *(uint *)(param_1 + 0x18c) = uVar7;
        plVar9 = plVar6;
        if (uVar7 == 0) {
          while (plVar6 = (longlong *)*plVar6, plVar6 != plVar9) {
            lVar5 = plVar6[2];
            if (*(int *)(lVar5 + 0x30) == 0) {
              lVar1 = *plVar6;
              *(longlong *)plVar6[1] = lVar1;
              *(longlong *)(lVar1 + 8) = plVar6[1];
              *(longlong *)(param_1 + 0x1a8) = *(longlong *)(param_1 + 0x1a8) + -1;
              FUN_1800b9d98(plVar6,0x18);
              FUN_1800540c0(lVar5);
              FUN_1800b9d98(lVar5,0x60);
              plVar6 = *(longlong **)(param_1 + 0x1a0);
              plVar9 = plVar6;
            }
          }
        }
        if ((*(byte *)(param_1 + 0x9d) & 1) == 0) {
          if ((*(byte *)(param_1 + 0x15) & 0x10) != 0) {
            return 0;
          }
          if (*(longlong *)(param_1 + 0x1a8) != 0) {
            return 0;
          }
          uVar7 = *(uint *)(param_1 + 0x1c);
          uVar2 = *(int *)(param_1 + 0xa0) * uVar7 + *(int *)(param_1 + 0xa4);
          uVar3 = uVar2 / uVar7;
          uVar2 = uVar2 % uVar7;
        }
        else {
          if (*(longlong *)(param_1 + 0x1a8) != 0) {
            return 0;
          }
          lVar5 = *(longlong *)(param_1 + 0x178) * 0x343fd;
          uVar8 = lVar5 + 0x100269ec2;
          if (-1 < (longlong)(lVar5 + 0x269ec3U)) {
            uVar8 = lVar5 + 0x269ec3U;
          }
          uVar10 = (lVar5 - (uVar8 & 0xffffffff00000000)) + 0x269ec3;
          uVar7 = *(uint *)(param_1 + 0x1c);
          lVar5 = uVar10 * 0x343fd;
          uVar8 = lVar5 + 0x100269ec2;
          if (-1 < (longlong)(lVar5 + 0x269ec3U)) {
            uVar8 = lVar5 + 0x269ec3U;
          }
          uVar8 = (lVar5 - (uVar8 & 0xffffffff00000000)) + 0x269ec3;
          *(ulonglong *)(param_1 + 0x178) = uVar8;
          uVar2 = (int)((longlong)((longlong)uVar10 >> 0x10 ^ uVar10) %
                       (longlong)*(int *)(param_1 + 0x18)) * uVar7 +
                  (int)((longlong)((longlong)uVar8 >> 0x10 ^ uVar8) % (longlong)(int)uVar7);
          uVar3 = uVar2 / uVar7;
          uVar2 = uVar2 % uVar7;
        }
        FUN_180054120(param_1,uVar3,uVar2,1);
        if ((*(int *)(param_1 + 0x18c) == 0) && (*(int *)(param_1 + 400) != -1)) {
          *(int *)(param_1 + 400) = *(int *)(param_1 + 400) + -1;
        }
        uVar4 = 0;
      }
    }
  }
  return uVar4;
}

```

## FUN_1800541d0 at `1800541d0`

Callers:
- none resolved

```c

undefined8 FUN_1800541d0(longlong param_1,uint *param_2,uint param_3,int param_4,int param_5)

{
  uint uVar1;
  uint uVar2;
  uint uVar3;
  uint uVar4;
  uint uVar5;
  uint uVar6;
  uint uVar7;
  ulonglong uVar8;
  
  if (param_3 == 0 || param_2 == (uint *)0x0) {
    return 0x80004005;
  }
  if (param_4 == 0 || param_5 == 0) {
    *(undefined4 *)(param_1 + 0x19c) = 0xffffffff;
  }
  else {
    uVar2 = *(uint *)(param_1 + 0x14);
    if ((uVar2 & 0x1000) == 0) {
      return 0;
    }
    if ((uVar2 & 4) == 0) {
      *(undefined4 *)(param_1 + 0x194) = 1;
    }
    else if (*(int *)(param_1 + 0x194) == 1) {
      *(undefined8 *)(param_1 + 0x18c) = 0;
    }
    else {
      *(undefined4 *)(param_1 + 0x194) = 1;
      *(undefined4 *)(param_1 + 400) = *(undefined4 *)(param_1 + 0x94);
      *(undefined4 *)(param_1 + 0x18c) = 0;
    }
    uVar4 = *param_2;
    if (uVar4 == 0xffffffff) {
      return 0;
    }
    if (param_3 == 1) {
      if ((uVar4 == *(uint *)(param_1 + 0x19c)) &&
         (1 < (uint)(*(int *)(param_1 + 0x18) * *(int *)(param_1 + 0x1c)))) {
        return 0;
      }
      *(uint *)(param_1 + 0x19c) = uVar4;
    }
    if ((uVar2 & 0x4000) == 0) {
      uVar1 = *(uint *)(param_1 + 0x1c);
      uVar2 = *param_2 / uVar1;
      uVar4 = *param_2 % uVar1;
      uVar6 = 0;
      if (1 < param_3) {
        uVar2 = *(uint *)(param_1 + 0x18);
        uVar8 = 0;
        uVar7 = 0;
        uVar4 = uVar1;
        do {
          uVar5 = param_2[uVar8];
          if (uVar5 != 0xffffffff) {
            uVar3 = uVar5 / uVar1;
            uVar5 = uVar5 % uVar1;
            if (uVar3 <= uVar2) {
              uVar2 = uVar3;
            }
            if (uVar7 < uVar3) {
              uVar7 = uVar3;
            }
            if (uVar5 <= uVar4) {
              uVar4 = uVar5;
            }
            if (uVar6 < uVar5) {
              uVar6 = uVar5;
            }
          }
          uVar8 = uVar8 + 1;
        } while (param_3 != uVar8);
        uVar2 = (uVar7 - uVar2 >> 1) + uVar2;
        uVar4 = (uVar6 - uVar4 >> 1) + uVar4;
      }
    }
    else {
      uVar2 = *(uint *)(param_1 + 0xa0);
      uVar4 = *(uint *)(param_1 + 0xa4);
    }
    FUN_180054120(param_1,uVar2,uVar4,*(undefined4 *)(param_1 + 0x94));
  }
  return 0;
}

```

## FUN_1800603b0 at `1800603b0`

Callers:
- none resolved

```c

ulonglong FUN_1800603b0(longlong param_1,undefined8 param_2,int param_3,int param_4,
                       undefined4 param_5,undefined8 param_6)

{
  uint uVar1;
  ulonglong uVar2;
  uint uVar3;
  uint uVar4;
  ulonglong uVar5;
  longlong lVar6;
  uint uVar7;
  uint uVar8;
  ulonglong uVar9;
  int iVar10;
  bool bVar11;
  
  uVar5 = FUN_1800691a0(param_1);
  if ((int)uVar5 < 0) {
    return uVar5;
  }
  uVar7 = *(uint *)(param_1 + 0x44);
  iVar10 = *(int *)(param_1 + 0x9c);
  *(int *)(param_1 + 0x14) = iVar10;
  uVar8 = (uint)(longlong)
                ((float)(uint)(param_4 * param_3 * *(int *)(param_1 + 0xa0)) / DAT_1801a9a70);
  if (uVar8 < 2) {
    uVar8 = 1;
  }
  *(uint *)(param_1 + 400) = uVar8;
  *(undefined4 *)(param_1 + 0x40) = 0;
  *(undefined4 *)(param_1 + 100) = 100;
  if (uVar7 - 0x65 < 0xffffff9c) {
LAB_180060442:
    *(undefined8 *)(param_1 + 0x40) = 0x3200000000;
    *(undefined4 *)(param_1 + 0x48) = 100;
    *(undefined8 *)(param_1 + 0x68) = 0xff00000000;
    *(undefined4 *)(param_1 + 0x70) = 0;
    *(undefined4 *)(param_1 + 0x180) = 3;
    *(undefined4 *)(param_1 + 0x9c) = 1;
    uVar8 = 3;
    bVar11 = false;
    iVar10 = 1;
  }
  else {
    uVar8 = 2;
    if (uVar7 != 100) {
      uVar1 = *(uint *)(param_1 + 0x48);
      if (uVar1 <= uVar7 || 100 < uVar1) goto LAB_180060442;
      uVar8 = 3;
      if (uVar1 != 100) {
        uVar3 = *(uint *)(param_1 + 0x4c);
        if (uVar3 <= uVar1 || 100 < uVar3) goto LAB_180060442;
        uVar8 = 4;
        if (uVar3 != 100) {
          uVar1 = *(uint *)(param_1 + 0x50);
          if (uVar1 <= uVar3 || 100 < uVar1) goto LAB_180060442;
          uVar8 = 5;
          if (uVar1 != 100) {
            uVar3 = *(uint *)(param_1 + 0x54);
            if (uVar3 <= uVar1 || 100 < uVar3) goto LAB_180060442;
            uVar8 = 6;
            if (uVar3 != 100) {
              uVar1 = *(uint *)(param_1 + 0x58);
              if (uVar1 <= uVar3 || 100 < uVar1) goto LAB_180060442;
              uVar8 = 7;
              if (uVar1 != 100) {
                uVar3 = *(uint *)(param_1 + 0x5c);
                if (uVar3 <= uVar1 || 100 < uVar3) goto LAB_180060442;
                uVar8 = 8;
                if (uVar3 != 100) {
                  uVar8 = *(uint *)(param_1 + 0x60);
                  if (uVar8 <= uVar3 || 100 < uVar8) goto LAB_180060442;
                  uVar8 = (uVar8 != 100) + 9;
                }
              }
            }
          }
        }
      }
    }
    *(uint *)(param_1 + 0x180) = uVar8;
    bVar11 = uVar7 == 100;
  }
  uVar7 = *(uint *)(param_1 + 0x90);
  uVar1 = *(uint *)(param_1 + 0xa4);
  uVar9 = 14000;
  if (uVar7 != 0) {
    uVar9 = (ulonglong)uVar7;
  }
  uVar2 = 1000 / (ulonglong)*(uint *)(param_1 + 0x38);
  uVar3 = (uint)(uVar9 / uVar2);
  if (uVar8 < uVar3) {
    uVar8 = uVar3;
  }
  *(uint *)(param_1 + 0x184) = uVar8;
  uVar4 = (uint)(uVar1 / uVar2);
  uVar3 = uVar8 >> 3;
  if (uVar8 >> 3 < uVar4) {
    uVar3 = uVar4;
  }
  *(uint *)(param_1 + 0x18c) = uVar3;
  uVar7 = (uint)*(uint3 *)(param_1 + 0x6c) + (uint)*(uint3 *)(param_1 + 0x68) +
          *(int *)(param_1 + 0xa0) + uVar7 + uVar1 + *(int *)(param_1 + 0x94) + iVar10 +
          *(int *)(param_1 + 0x98);
  if (((((!bVar11) && (uVar7 = uVar7 + *(uint3 *)(param_1 + 0x70), *(int *)(param_1 + 0x48) != 100))
       && (uVar7 = uVar7 + *(uint3 *)(param_1 + 0x74), *(int *)(param_1 + 0x4c) != 100)) &&
      ((uVar7 = uVar7 + *(uint3 *)(param_1 + 0x78), *(int *)(param_1 + 0x50) != 100 &&
       (uVar7 = uVar7 + *(uint3 *)(param_1 + 0x7c), *(int *)(param_1 + 0x54) != 100)))) &&
     ((uVar7 = uVar7 + *(uint3 *)(param_1 + 0x80), *(int *)(param_1 + 0x58) != 100 &&
      ((uVar7 = uVar7 + *(uint3 *)(param_1 + 0x84), *(int *)(param_1 + 0x5c) != 100 &&
       (uVar7 = uVar7 + *(uint3 *)(param_1 + 0x88), *(int *)(param_1 + 0x60) != 100)))))) {
    uVar7 = uVar7 + *(uint3 *)(param_1 + 0x8c);
  }
  lVar6 = FUN_18014bdf0(0,(ulonglong)uVar1 % uVar2,uVar9,uVar2,param_5,param_6);
  *(ulonglong *)(param_1 + 0x178) = (lVar6 / 1800000) * 1800000 + (ulonglong)uVar7 * 0x1f;
  return uVar5 & 0xffffffff;
}

```

## FUN_180060790 at `180060790`

Callers:
- none resolved

```c

undefined8 FUN_180060790(longlong param_1,longlong param_2)

{
  longlong *plVar1;
  longlong lVar2;
  longlong *plVar3;
  undefined8 uVar4;
  longlong *plVar5;
  longlong *plVar6;
  longlong *plVar7;
  bool bVar8;
  
  if (param_2 == 0) {
    uVar4 = 0x80004005;
  }
  else {
    plVar1 = (longlong *)(param_1 + 0x198);
    plVar3 = (longlong *)**(longlong **)(param_1 + 0x198);
    if (plVar3 != *(longlong **)(param_1 + 0x198)) {
      do {
        FUN_180060900(plVar3[5]);
        plVar6 = (longlong *)plVar3[2];
        plVar7 = plVar3;
        if (*(char *)(plVar3[2] + 0x19) == '\0') {
          do {
            plVar3 = plVar6;
            plVar6 = (longlong *)*plVar3;
          } while (*(char *)(*plVar3 + 0x19) == '\0');
        }
        else {
          do {
            plVar3 = (longlong *)plVar7[1];
            if (*(char *)((longlong)plVar3 + 0x19) != '\0') break;
            bVar8 = plVar7 == (longlong *)plVar3[2];
            plVar7 = plVar3;
          } while (bVar8);
        }
      } while (plVar3 != (longlong *)*plVar1);
    }
    if (*(int *)(param_1 + 0x188) == 0) {
      FUN_180060950(param_1);
      plVar3 = *(longlong **)(param_1 + 0x198);
      plVar6 = (longlong *)*plVar3;
      if ((longlong *)*plVar3 != plVar3) {
        do {
          while ((lVar2 = plVar6[5], lVar2 != 0 && (*(int *)(lVar2 + 0x18) == 0))) {
            uVar4 = FUN_1800614d0(plVar1);
            FUN_1800b9d98(uVar4,0x30);
            FUN_180060380(lVar2);
            FUN_1800b9d98(lVar2,0x28);
            plVar3 = (longlong *)*plVar1;
            plVar6 = (longlong *)*plVar3;
            if (plVar6 == plVar3) goto LAB_180060817;
          }
          plVar7 = (longlong *)plVar6[2];
          if (*(char *)(plVar6[2] + 0x19) == '\0') {
            do {
              plVar5 = plVar7;
              plVar7 = (longlong *)*plVar5;
            } while (*(char *)(*plVar5 + 0x19) == '\0');
          }
          else {
            do {
              plVar5 = (longlong *)plVar6[1];
              if (*(char *)((longlong)plVar5 + 0x19) != '\0') break;
              bVar8 = plVar6 == (longlong *)plVar5[2];
              plVar6 = plVar5;
            } while (bVar8);
          }
          plVar6 = plVar5;
        } while (plVar5 != plVar3);
      }
    }
LAB_180060817:
    uVar4 = 0;
    *(uint *)(param_1 + 0x188) = (*(int *)(param_1 + 0x188) + 1U) % *(uint *)(param_1 + 0x18c);
  }
  return uVar4;
}

```

## FUN_180061e90 at `180061e90`

Callers:
- none resolved

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_180061e90(longlong param_1,longlong param_2)

{
  uint uVar1;
  ulonglong uVar2;
  undefined8 uVar3;
  ulonglong uVar4;
  void *pvVar5;
  uint uVar6;
  uint uVar7;
  ulonglong uVar8;
  
  uVar4 = FUN_1800691a0(param_1);
  if ((int)uVar4 < 0) {
    return uVar4;
  }
  uVar6 = *(uint *)(param_1 + 0x44);
  *(undefined4 *)(param_1 + 0x14) = *(undefined4 *)(param_1 + 0x9c);
  *(undefined4 *)(param_1 + 0x40) = 0;
  *(undefined4 *)(param_1 + 100) = 100;
  if (uVar6 - 0x65 < 0xffffff9c) {
LAB_180061ef2:
    *(undefined4 *)(param_1 + 0x180) = 0;
    uVar3 = _UNK_1801ac668;
    *(undefined8 *)(param_1 + 0x40) = _DAT_1801ac660;
    *(undefined8 *)(param_1 + 0x48) = uVar3;
    uVar3 = _UNK_1801ac678;
    *(undefined8 *)(param_1 + 0x68) = _DAT_1801ac670;
    *(undefined8 *)(param_1 + 0x70) = uVar3;
    uVar7 = 0;
  }
  else {
    uVar7 = 2;
    if (uVar6 != 100) {
      uVar1 = *(uint *)(param_1 + 0x48);
      if (uVar1 <= uVar6 || 100 < uVar1) goto LAB_180061ef2;
      uVar7 = 3;
      if (uVar1 != 100) {
        uVar6 = *(uint *)(param_1 + 0x4c);
        if (uVar6 <= uVar1 || 100 < uVar6) goto LAB_180061ef2;
        uVar7 = 4;
        if (uVar6 != 100) {
          uVar1 = *(uint *)(param_1 + 0x50);
          if (uVar1 <= uVar6 || 100 < uVar1) goto LAB_180061ef2;
          uVar7 = 5;
          if (uVar1 != 100) {
            uVar6 = *(uint *)(param_1 + 0x54);
            if (uVar6 <= uVar1 || 100 < uVar6) goto LAB_180061ef2;
            uVar7 = 6;
            if (uVar6 != 100) {
              uVar1 = *(uint *)(param_1 + 0x58);
              if (uVar1 <= uVar6 || 100 < uVar1) goto LAB_180061ef2;
              uVar7 = 7;
              if (uVar1 != 100) {
                uVar6 = *(uint *)(param_1 + 0x5c);
                if (uVar6 <= uVar1 || 100 < uVar6) goto LAB_180061ef2;
                uVar7 = 8;
                if (uVar6 != 100) {
                  uVar7 = *(uint *)(param_1 + 0x60);
                  if (uVar7 <= uVar6 || 100 < uVar7) goto LAB_180061ef2;
                  uVar7 = (uVar7 != 100) + 9;
                }
              }
            }
          }
        }
      }
    }
    *(uint *)(param_1 + 0x180) = uVar7;
  }
  *(undefined4 *)(param_1 + 0x18c) = *(undefined4 *)(param_1 + 0x94);
  uVar6 = 0x936c;
  if (*(uint *)(param_1 + 0x90) != 0) {
    uVar6 = *(uint *)(param_1 + 0x90);
  }
  uVar2 = (ulonglong)uVar6 / (1000 / (ulonglong)*(uint *)(param_1 + 0x38));
  uVar8 = (ulonglong)uVar7;
  if (uVar7 < (uint)uVar2) {
    uVar8 = uVar2;
  }
  *(int *)(param_1 + 0x184) = (int)uVar8;
  pvVar5 = operator_new(uVar8 * 4);
  *(void **)(param_1 + 0x198) = pvVar5;
  FUN_180194db0(pvVar5,0,uVar8 * 4);
  FUN_1800692e0(param_1,10,(undefined8 *)(param_1 + 0x40),param_1 + 0x68,(int)uVar8,pvVar5);
  uVar6 = *(uint *)(param_1 + 0x14);
  if ((uVar6 & 0x1000) != 0 && param_2 != 0) {
    FUN_18004cde0(param_2,1);
    uVar6 = *(uint *)(param_1 + 0x14);
  }
  *(uint *)(param_1 + 400) = (uint)((uVar6 & 0x1000) == 0);
  return uVar4 & 0xffffffff;
}

```

## FUN_180062130 at `180062130`

Callers:
- none resolved

```c

undefined8 FUN_180062130(longlong param_1,longlong param_2)

{
  ulonglong uVar1;
  uint uVar2;
  
  if (param_2 == 0) {
    return 0x80004005;
  }
  if (*(int *)(param_1 + 400) != 0) {
    if (*(int *)(param_1 + 0x18c) == 0) {
      *(undefined4 *)(param_1 + 400) = 0;
      return 0;
    }
    if (*(int *)(param_1 + 0x1c) * *(int *)(param_1 + 0x18) != 0) {
      uVar1 = 0;
      do {
        *(undefined4 *)(param_2 + uVar1 * 4) =
             *(undefined4 *)
              (*(longlong *)(param_1 + 0x198) + (ulonglong)*(uint *)(param_1 + 0x188) * 4);
        uVar1 = uVar1 + 1;
      } while (uVar1 < (uint)(*(int *)(param_1 + 0x1c) * *(int *)(param_1 + 0x18)));
    }
    uVar2 = (*(int *)(param_1 + 0x188) + 1U) % *(uint *)(param_1 + 0x184);
    *(uint *)(param_1 + 0x188) = uVar2;
    if ((uVar2 == 0) && (*(int *)(param_1 + 0x18c) != -1)) {
      *(int *)(param_1 + 0x18c) = *(int *)(param_1 + 0x18c) + -1;
      return 0;
    }
  }
  return 0;
}

```

## FUN_1800621d0 at `1800621d0`

Callers:
- none resolved

```c

undefined8 FUN_1800621d0(longlong param_1)

{
  int in_stack_00000028;
  
  if (((in_stack_00000028 != 0) && ((*(byte *)(param_1 + 0x15) & 0x10) != 0)) &&
     (*(int *)(param_1 + 400) != 1)) {
    *(undefined4 *)(param_1 + 400) = 1;
    *(undefined4 *)(param_1 + 0x18c) = *(undefined4 *)(param_1 + 0x94);
    *(undefined4 *)(param_1 + 0x188) = 0;
  }
  return 0;
}

```

## FUN_1800624a0 at `1800624a0`

Callers:
- none resolved

```c

void FUN_1800624a0(longlong *param_1,undefined8 param_2,undefined4 param_3,undefined4 param_4,
                  undefined8 param_5,undefined8 param_6)

{
  int iVar1;
  uint uVar2;
  longlong lVar3;
  
  iVar1 = FUN_1800691a0(param_1);
  if (-1 < iVar1) {
    uVar2 = *(uint *)((longlong)param_1 + 0x9c);
    *(undefined4 *)((longlong)param_1 + 0x14) = *(undefined4 *)((longlong)param_1 + 0x94);
    *(undefined4 *)((longlong)param_1 + 100) = 100;
    param_1[8] = 0x3200000000;
    *(undefined4 *)(param_1 + 9) = 100;
    *(undefined2 *)(param_1 + 0xe) = 0;
    *(undefined1 *)((longlong)param_1 + 0x72) = 0;
    *(undefined4 *)(param_1 + 0x30) = 3;
    if (uVar2 < 0x28) {
      *(undefined4 *)((longlong)param_1 + 0x9c) = 0x28;
      uVar2 = 0x28;
    }
    iVar1 = (int)param_1[0x13];
    if (iVar1 == 0) {
      *(undefined4 *)(param_1 + 0x13) = 0x32;
      iVar1 = 0x32;
    }
    *(uint *)((longlong)param_1 + 0x18c) =
         -(uint)(*(uint *)(param_1 + 0x12) == 0) | *(uint *)(param_1 + 0x12);
    *(uint *)((longlong)param_1 + 0x184) = (uVar2 / 0x28 + 1) * iVar1;
    lVar3 = FUN_18014bdf0(0);
    param_1[0x2f] = (lVar3 / 1800000) * 1800000 | 0x1f;
    (**(code **)(*param_1 + 0x48))(param_1,0,0,param_4,param_3,param_6);
  }
  return;
}

```

## FUN_1800625d0 at `1800625d0`

Callers:
- none resolved

```c

undefined8 FUN_1800625d0(longlong param_1)

{
  undefined4 *puVar1;
  undefined8 *puVar2;
  undefined8 *puVar3;
  byte bVar4;
  undefined4 uVar5;
  undefined4 uVar6;
  undefined4 uVar7;
  undefined4 uVar8;
  undefined8 uVar9;
  undefined8 uVar10;
  undefined8 uVar11;
  undefined8 uVar12;
  float fVar13;
  char cVar14;
  int iVar15;
  uint uVar16;
  undefined8 uVar17;
  void *pvVar18;
  void *pvVar19;
  uint uVar20;
  ushort uVar21;
  int iVar22;
  ulonglong uVar23;
  byte *pbVar24;
  longlong lVar25;
  __uint64 _Var26;
  longlong lVar27;
  undefined1 *puVar28;
  longlong lVar29;
  uint uVar30;
  ulonglong uVar31;
  int iVar32;
  int iVar33;
  void *pvVar34;
  ulonglong uVar35;
  ulonglong uVar36;
  uint uVar37;
  uint3 *puVar38;
  bool bVar39;
  float fVar40;
  undefined4 in_stack_00000028;
  undefined1 auStack_118 [32];
  int local_f8;
  void *local_f0;
  ulonglong local_e8;
  ulonglong local_e0;
  void *local_d8;
  ulonglong local_d0;
  uint local_c8;
  uint local_c4;
  longlong local_c0;
  undefined8 local_b8;
  undefined8 uStack_b0;
  undefined8 local_a8;
  undefined8 uStack_a0;
  undefined8 local_98;
  undefined8 local_88;
  undefined8 uStack_80;
  undefined8 local_78;
  undefined8 uStack_70;
  undefined8 local_68;
  ulonglong local_60;
  
  local_60 = DAT_1801f4b40 ^ (ulonglong)auStack_118;
  local_f8 = in_stack_00000028;
  uVar17 = FUN_180069290(param_1);
  if ((int)uVar17 < 0) goto LAB_1800631b4;
  if (*(longlong *)(param_1 + 400) != 0) {
    FUN_1800b9de0();
  }
  *(undefined8 *)(param_1 + 400) = 0;
  iVar15 = *(int *)(param_1 + 0x184);
  _Var26 = (ulonglong)(uint)(*(int *)(param_1 + 0x18) * iVar15 * *(int *)(param_1 + 0x1c)) << 2;
  pvVar18 = operator_new(_Var26);
  *(void **)(param_1 + 400) = pvVar18;
  FUN_180194db0(pvVar18,0,_Var26);
  _Var26 = (ulonglong)(uint)(iVar15 * 0xa1) << 2;
  pvVar18 = operator_new(_Var26);
  FUN_180194db0(pvVar18,0,_Var26);
  pvVar19 = operator_new(0x400);
  FUN_180194db0(pvVar19,0,0x400);
  if (*(int *)(param_1 + 0x98) == 0) {
LAB_180062915:
    uVar37 = *(uint *)(param_1 + 0x9c);
    iVar15 = *(int *)(param_1 + 0x184);
    uVar23 = (ulonglong)(uVar37 / 0x28 + 1);
    uVar35 = 0xffffffff;
LAB_18006296a:
    local_c0 = param_1 + 0x40;
    iVar32 = (int)uVar23;
    iVar33 = (int)uVar35;
    iVar22 = iVar15;
    if ((uVar35 & 1) != 0) {
      iVar22 = iVar15 - iVar32;
      FUN_180194710((void *)((longlong)pvVar18 + (ulonglong)(uint)(iVar22 * 0xa1) * 4),
                    (void *)((longlong)pvVar18 + (ulonglong)(uint)(iVar33 * 0xa1) * 4),0x284);
      uVar35 = uVar35 - 1;
    }
    if (iVar33 != 1) {
      local_e0 = CONCAT44(local_e0._4_4_,uVar37);
      local_d8 = (void *)CONCAT44(local_d8._4_4_,iVar15);
      uVar31 = uVar35 * 0xa1;
      uVar16 = iVar22 * 0xa1 + iVar32 * -0x142;
      uVar37 = iVar22 * 0xa1 + iVar32 * -0xa1;
      local_d0 = uVar23;
      do {
        uVar35 = uVar35 - 2;
        local_e8 = uVar35;
        FUN_180194710((void *)((longlong)pvVar18 + (ulonglong)uVar37 * 4),
                      (void *)((longlong)pvVar18 + (uVar31 & 0xffffffff) * 4),0x284);
        FUN_180194710((void *)((longlong)pvVar18 + (ulonglong)uVar16 * 4),
                      (void *)((longlong)pvVar18 + (ulonglong)((int)uVar31 - 0xa1) * 4),0x284);
        uVar31 = uVar31 - 0x142;
        uVar16 = uVar16 + iVar32 * -0x142;
        uVar37 = uVar37 + iVar32 * -0x142;
      } while ((uint)local_e8 != 0);
      uVar23 = local_d0;
      uVar37 = (uint)local_e0;
      iVar15 = (int)local_d8;
    }
  }
  else {
    uVar16 = 0x8a;
    pvVar34 = (void *)0x0;
    do {
      local_e8 = CONCAT44(local_e8._4_4_,uVar16);
      lVar25 = 0;
      do {
        iVar15 = FUN_1801551d0();
        iVar15 = iVar15 + (((uint)(iVar15 / 6 + (iVar15 >> 0x1f)) >> 5) - (iVar15 >> 0x1f)) * -0xc0
                 + 0x20;
        cVar14 = (char)iVar15 - (char)(((uint)(byte)(&DAT_1801f534a)[lVar25] * iVar15) / 0xff);
        *(char *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar16 * 4) = cVar14;
        *(char *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar16 * 4 + 1) = cVar14;
        *(char *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar16 * 4 + 2) = cVar14;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      pvVar34 = (void *)((longlong)pvVar34 + 1);
      uVar37 = *(uint *)(param_1 + 0x98);
      uVar16 = (uint)local_e8 + 0xa1;
    } while (pvVar34 < (void *)(ulonglong)uVar37);
    local_c0 = param_1 + 0x40;
    uVar16 = uVar37 - 1;
    if (uVar37 == 0) goto LAB_180062915;
    local_d8 = (void *)(ulonglong)uVar16;
    local_e0 = (longlong)pvVar18 + 2;
    lVar25 = 6;
    puVar28 = &DAT_1801f5333;
    uVar30 = 0x8a;
    do {
      local_c4 = uVar30;
      iVar15 = (int)lVar25 * 0x17;
      lVar25 = lVar25 + -1;
      local_e8 = CONCAT44(local_e8._4_4_,iVar15 + 0x8a);
      pvVar34 = (void *)0x0;
      uVar30 = local_c4;
      do {
        iVar22 = (int)pvVar34 * 0xa1;
        uVar35 = (ulonglong)(uint)(iVar22 + iVar15);
        uVar20 = iVar22 + iVar15 + 0x8a;
        if (pvVar34 == local_d8) {
          uVar20 = iVar15 - 0x17;
        }
        uVar23 = (ulonglong)uVar20;
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar35 * 4 + 0x4c);
        uVar17 = puVar2[1];
        puVar3 = (undefined8 *)((longlong)pvVar18 + uVar23 * 4 + 0x4c);
        *puVar3 = *puVar2;
        puVar3[1] = uVar17;
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar35 * 4 + 0x40);
        uVar17 = puVar2[1];
        puVar3 = (undefined8 *)((longlong)pvVar18 + uVar23 * 4 + 0x40);
        *puVar3 = *puVar2;
        puVar3[1] = uVar17;
        puVar1 = (undefined4 *)((longlong)pvVar18 + uVar35 * 4);
        uVar5 = *puVar1;
        uVar6 = puVar1[1];
        uVar7 = puVar1[2];
        uVar8 = puVar1[3];
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar35 * 4 + 0x10);
        uVar17 = *puVar2;
        uVar9 = puVar2[1];
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar35 * 4 + 0x20);
        uVar10 = *puVar2;
        uVar11 = puVar2[1];
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar35 * 4 + 0x30);
        uVar12 = puVar2[1];
        puVar3 = (undefined8 *)((longlong)pvVar18 + uVar23 * 4 + 0x30);
        *puVar3 = *puVar2;
        puVar3[1] = uVar12;
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar23 * 4 + 0x20);
        *puVar2 = uVar10;
        puVar2[1] = uVar11;
        puVar2 = (undefined8 *)((longlong)pvVar18 + uVar23 * 4 + 0x10);
        *puVar2 = uVar17;
        puVar2[1] = uVar9;
        puVar1 = (undefined4 *)((longlong)pvVar18 + uVar23 * 4);
        *puVar1 = uVar5;
        puVar1[1] = uVar6;
        puVar1[2] = uVar7;
        puVar1[3] = uVar8;
        lVar27 = local_e0 + uVar23 * 4;
        lVar29 = 0;
        do {
          while (uVar21 = (ushort)*(byte *)((longlong)pvVar18 + lVar29 * 4 + (ulonglong)uVar30 * 4),
                uVar20 = (uint)(byte)puVar28[lVar29], (ushort)((uVar20 * uVar21) / 0xff) < uVar21) {
            bVar4 = *(byte *)(lVar27 + -2 + lVar29 * 4);
            *(byte *)(lVar27 + -2 + lVar29 * 4) = bVar4 - (char)((bVar4 * uVar20) / 0xff);
            bVar4 = *(byte *)(lVar27 + -1 + lVar29 * 4);
            *(byte *)(lVar27 + -1 + lVar29 * 4) = bVar4 - (char)((bVar4 * uVar20) / 0xff);
            bVar4 = *(byte *)(lVar27 + lVar29 * 4);
            *(byte *)(lVar27 + lVar29 * 4) = bVar4 - (char)((bVar4 * uVar20) / 0xff);
            lVar29 = lVar29 + 1;
            if (lVar29 == 0x17) goto LAB_1800627e0;
          }
          *(undefined2 *)(lVar27 + -1 + lVar29 * 4) = 0x101;
          *(undefined1 *)(lVar27 + -2 + lVar29 * 4) = 1;
          lVar29 = lVar29 + 1;
        } while (lVar29 != 0x17);
LAB_1800627e0:
        pvVar34 = (void *)((longlong)pvVar34 + 1);
        uVar30 = uVar30 + 0xa1;
      } while (pvVar34 != (void *)(ulonglong)uVar37);
      puVar28 = puVar28 + -0x17;
      uVar30 = local_c4 - 0x17;
    } while (lVar25 != 0);
    uVar37 = *(uint *)(param_1 + 0x9c);
    iVar15 = *(int *)(param_1 + 0x184);
    uVar23 = (ulonglong)(uVar37 / 0x28 + 1);
    uVar35 = (ulonglong)uVar16;
    local_d0 = 0;
    local_c8 = uVar16;
    if (uVar16 != 0) goto LAB_18006296a;
  }
  iVar22 = (int)uVar23;
  uVar16 = iVar15 - iVar22;
  local_d8 = pvVar18;
  if (uVar37 < 0x28) {
    local_d0 = CONCAT44(local_d0._4_4_,iVar22 * 0xa1);
    uVar30 = 0;
    uVar37 = 0;
    do {
      pvVar18 = local_d8;
      local_e8 = CONCAT44(local_e8._4_4_,uVar30);
      local_e0 = CONCAT44(local_e0._4_4_,uVar37 + iVar22);
      uVar20 = (uVar37 + iVar22) * 0xa1;
      if (uVar37 == uVar16) {
        uVar20 = 0;
      }
      lVar25 = 0;
      do {
        uStack_80 = 0;
        local_78 = 0;
        uStack_70 = 0;
        local_68 = 0;
        uStack_b0 = 0;
        local_a8 = 0;
        uStack_a0 = 0;
        local_98 = 0;
        local_88 = 0x6400000000;
        local_b8._0_4_ =
             (uint)CONCAT12(*(undefined1 *)
                             ((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar30 * 4 + 2),
                            CONCAT11(*(undefined1 *)
                                      ((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar30 * 4 + 1),
                                     *(undefined1 *)
                                      ((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar30 * 4)));
        local_b8._0_5_ =
             CONCAT14(*(undefined1 *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar20 * 4),
                      (uint)local_b8);
        local_b8._0_6_ =
             CONCAT15(*(undefined1 *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar20 * 4 + 1),
                      (undefined5)local_b8);
        local_b8._0_7_ =
             CONCAT16(*(undefined1 *)((longlong)pvVar18 + lVar25 * 4 + (ulonglong)uVar20 * 4 + 2),
                      (undefined6)local_b8);
        local_b8 = (ulonglong)(uint7)local_b8;
        local_f8 = iVar22;
        local_f0 = pvVar19;
        FUN_1800692e0(param_1,10,&local_88,&local_b8);
        lVar25 = lVar25 + 1;
      } while ((int)lVar25 != 0xa1);
      uVar16 = *(int *)(param_1 + 0x184) - iVar22;
      uVar30 = (uint)local_e8 + (int)local_d0;
      uVar37 = (uint)local_e0;
    } while ((uint)local_e0 <= uVar16);
  }
  else {
    local_e8 = (uVar23 & 0xffffffff) - 1;
    uVar35 = local_e8 & 0xfffffffffffffffe;
    uVar37 = 0;
    do {
      puVar38 = (uint3 *)((longlong)local_d8 + (ulonglong)(uVar37 * 0xa1) * 4);
      local_e0 = CONCAT44(local_e0._4_4_,uVar37 + iVar22);
      uVar30 = (uVar37 + iVar22) * 0xa1;
      if (uVar37 == uVar16) {
        uVar30 = 0;
      }
      puVar28 = (undefined1 *)((longlong)local_d8 + (ulonglong)uVar30 * 4);
      iVar15 = 0;
      do {
        uStack_80 = 0;
        local_78 = 0;
        uStack_70 = 0;
        local_68 = 0;
        uStack_b0 = 0;
        local_a8 = 0;
        uStack_a0 = 0;
        local_98 = 0;
        local_88 = 0x6400000000;
        local_b8._0_4_ = (uint)*puVar38;
        local_b8._0_5_ = CONCAT14(*puVar28,(uint)local_b8);
        local_b8._0_6_ = CONCAT15(puVar28[1],(undefined5)local_b8);
        local_b8._0_7_ = CONCAT16(puVar28[2],(undefined6)local_b8);
        local_b8 = (ulonglong)(uint7)local_b8;
        local_f8 = iVar22;
        local_f0 = pvVar19;
        FUN_1800692e0(param_1,10,&local_88,&local_b8);
        lVar25 = 1;
        if (iVar22 != 2) {
          uVar16 = 0xa1;
          do {
            uVar23 = (ulonglong)uVar16;
            *(undefined1 *)(puVar38 + uVar23) = *(undefined1 *)((longlong)pvVar19 + lVar25 * 4);
            *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 1) =
                 *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 1);
            *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 2) =
                 *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 2);
            uVar23 = (ulonglong)(uVar16 + 0xa1);
            *(undefined1 *)(puVar38 + uVar23) = *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 4);
            *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 1) =
                 *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 5);
            *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 2) =
                 *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 6);
            uVar16 = uVar16 + 0x142;
            lVar27 = lVar25 - uVar35;
            lVar25 = lVar25 + 2;
          } while (lVar27 != -1);
        }
        if ((local_e8 & 1) != 0) {
          uVar23 = (ulonglong)(uint)((int)lVar25 * 0xa1);
          *(undefined1 *)(puVar38 + uVar23) = *(undefined1 *)((longlong)pvVar19 + lVar25 * 4);
          *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 1) =
               *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 1);
          *(undefined1 *)((longlong)puVar38 + uVar23 * 4 + 2) =
               *(undefined1 *)((longlong)pvVar19 + lVar25 * 4 + 2);
        }
        puVar38 = puVar38 + 1;
        puVar28 = puVar28 + 4;
        iVar15 = iVar15 + 1;
      } while (iVar15 != 0xa1);
      uVar16 = *(int *)(param_1 + 0x184) - iVar22;
      uVar37 = (uint)local_e0;
    } while ((uint)local_e0 <= uVar16);
  }
  uVar35 = 0;
  FUN_180194db0(pvVar19,0,0x400);
  local_f8 = 0x100;
  local_f0 = pvVar19;
  FUN_1800692e0(param_1,10);
  pvVar18 = local_d8;
  if (*(int *)(param_1 + 0x184) != 0) {
    uVar23 = 0;
    do {
      lVar25 = 0;
      lVar27 = 0;
      do {
        uVar31 = (ulonglong)(byte)(&DAT_1801f5370)[lVar27];
        *(char *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4) =
             (char)(((uint)*(byte *)((longlong)pvVar19 + uVar31 * 4) *
                    (uint)*(byte *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4)) / 0xff);
        *(char *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4 + 1) =
             (char)(((uint)*(byte *)((longlong)pvVar19 + uVar31 * 4 + 1) *
                    (uint)*(byte *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4 + 1)) / 0xff);
        *(char *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4 + 2) =
             (char)(((uint)*(byte *)((longlong)pvVar19 + uVar31 * 4 + 2) *
                    (uint)*(byte *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4 + 2)) / 0xff);
        *(undefined1 *)((longlong)local_d8 + lVar27 * 4 + uVar35 * 4 + 3) =
             *(undefined1 *)((longlong)pvVar19 + uVar31 * 4 + 3);
        lVar27 = lVar27 + 1;
        lVar25 = lVar25 + -4;
      } while (lVar27 != 0x17);
      pbVar24 = (byte *)((longlong)local_d8 + (uVar35 * 4 - lVar25));
      lVar25 = 0;
      do {
        bVar4 = (&DAT_1801f5387)[lVar25];
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 + (ulonglong)bVar4 * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 + (ulonglong)bVar4 * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f5387)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f5387)[lVar25] * 4 + 3
                              );
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      lVar25 = 0;
      do {
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                          (ulonglong)(byte)(&DAT_1801f539e)[lVar25] * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f539e)[lVar25] * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f539e)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f539e)[lVar25] * 4 + 3
                              );
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      lVar25 = 0;
      do {
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                          (ulonglong)(byte)(&DAT_1801f53b5)[lVar25] * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53b5)[lVar25] * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53b5)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f53b5)[lVar25] * 4 + 3
                              );
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      lVar25 = 0;
      do {
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                          (ulonglong)(byte)(&DAT_1801f53cc)[lVar25] * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53cc)[lVar25] * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53cc)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f53cc)[lVar25] * 4 + 3
                              );
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      lVar25 = 0;
      do {
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                          (ulonglong)(byte)(&DAT_1801f53e3)[lVar25] * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53e3)[lVar25] * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53e3)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f53e3)[lVar25] * 4 + 3
                              );
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      lVar25 = 0;
      do {
        *pbVar24 = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                          (ulonglong)(byte)(&DAT_1801f53fa)[lVar25] * 4) *
                          (uint)*pbVar24) / 0xff);
        pbVar24[1] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53fa)[lVar25] * 4 + 1) *
                            (uint)pbVar24[1]) / 0xff);
        pbVar24[2] = (byte)(((uint)*(byte *)((longlong)pvVar19 +
                                            (ulonglong)(byte)(&DAT_1801f53fa)[lVar25] * 4 + 2) *
                            (uint)pbVar24[2]) / 0xff);
        pbVar24[3] = *(byte *)((longlong)pvVar19 + (ulonglong)(byte)(&DAT_1801f53fa)[lVar25] * 4 + 3
                              );
        fVar13 = DAT_1801ac74c;
        pbVar24 = pbVar24 + 4;
        lVar25 = lVar25 + 1;
      } while (lVar25 != 0x17);
      uVar23 = uVar23 + 1;
      uVar35 = (ulonglong)((int)uVar35 + 0xa1);
    } while (uVar23 < *(uint *)(param_1 + 0x184));
    if (*(uint *)(param_1 + 0x184) != 0) {
      uVar23 = (ulonglong)*(uint *)(param_1 + 0x30);
      uVar36 = 0;
      local_e8 = CONCAT44(local_e8._4_4_,0x8a);
      uVar31 = (ulonglong)*(uint *)(param_1 + 0x30);
      uVar35 = uVar23;
      do {
        iVar15 = (*(int *)(param_1 + 0x18) * (int)uVar36 + *(int *)(param_1 + 0x24)) *
                 *(int *)(param_1 + 0x1c) + *(int *)(param_1 + 0x20);
        local_e0 = uVar36;
        if ((*(uint *)(param_1 + 0x34) < 0x17) && ((uint)uVar31 < 7)) {
          if ((int)uVar35 == 0) {
            uVar35 = 0;
            uVar23 = 0;
LAB_18006301d:
            uVar31 = 0;
          }
          else {
            iVar22 = -1;
            uVar36 = 0;
            uVar23 = uVar35;
            uVar16 = (uint)local_e8;
            do {
              FUN_180194710((ulonglong)
                            (uint)(((int)uVar23 + iVar22) * *(int *)(param_1 + 0x1c) + iVar15) * 4 +
                            *(longlong *)(param_1 + 400),
                            (void *)((longlong)pvVar18 + (ulonglong)uVar16 * 4),
                            (ulonglong)*(uint *)(param_1 + 0x34) << 2);
              uVar36 = uVar36 + 1;
              uVar23 = (ulonglong)*(uint *)(param_1 + 0x30);
              uVar16 = uVar16 - 0x17;
              iVar22 = iVar22 + -1;
              uVar31 = (ulonglong)*(uint *)(param_1 + 0x30);
              uVar35 = uVar23;
            } while (uVar36 < uVar23);
          }
        }
        else {
          if ((uint)uVar31 == 0) goto LAB_18006301d;
          if (*(uint *)(param_1 + 0x34) != 0) {
            fVar40 = (float)uVar31;
            uVar37 = 0;
            uVar16 = 1;
            iVar22 = -1;
            do {
              bVar39 = uVar16 != 0;
              uVar16 = 0;
              if (bVar39) {
                uVar30 = 0;
                do {
                  *(undefined4 *)
                   (*(longlong *)(param_1 + 400) +
                   (ulonglong)
                   ((*(int *)(param_1 + 0x30) + iVar22) * *(int *)(param_1 + 0x1c) + iVar15 + uVar30
                   ) * 4) = *(undefined4 *)
                             ((longlong)pvVar18 +
                             (ulonglong)
                             (uVar30 / 0x17 + (uVar30 / 0x17) * -0x18 + uVar30 +
                             (int)(longlong)((float)uVar37 * (fVar13 / fVar40)) * -0x17 +
                             (uint)local_e8) * 4);
                  uVar30 = uVar30 + 1;
                  uVar16 = *(uint *)(param_1 + 0x34);
                } while (uVar30 < uVar16);
                uVar23 = (ulonglong)*(uint *)(param_1 + 0x30);
                uVar35 = uVar23;
              }
              uVar37 = uVar37 + 1;
              iVar22 = iVar22 + -1;
              uVar31 = uVar23;
            } while (uVar37 < (uint)uVar23);
          }
        }
        uVar16 = (int)local_e0 + 1;
        uVar36 = (ulonglong)uVar16;
        local_e8 = CONCAT44(local_e8._4_4_,(uint)local_e8 + 0xa1);
      } while (uVar16 < *(uint *)(param_1 + 0x184));
    }
  }
  FUN_1800b9de0(pvVar19);
  FUN_1800b9de0(pvVar18);
  uVar17 = 0;
LAB_1800631b4:
  if ((local_60 ^ (ulonglong)auStack_118) != DAT_1801f4b40) {
                    /* WARNING: Subroutine does not return */
    FUN_1800b9f70();
  }
  return uVar17;
}

```

## FUN_1800633b0 at `1800633b0`

Callers:
- none resolved

```c

undefined8 FUN_1800633b0(longlong param_1,longlong param_2)

{
  uint uVar1;
  
  if (param_2 == 0) {
    return 0x80004005;
  }
  if (*(int *)(param_1 + 0x18c) != 0) {
    if (*(longlong *)(param_1 + 400) != 0) {
      uVar1 = *(int *)(param_1 + 0x1c) * *(int *)(param_1 + 0x18);
      FUN_180194710(param_2,*(longlong *)(param_1 + 400) +
                            (ulonglong)(*(int *)(param_1 + 0x188) * uVar1) * 4,(ulonglong)uVar1 << 2
                   );
    }
    if ((*(int *)(param_1 + 0x188) == 0) && (*(int *)(param_1 + 0x18c) != -1)) {
      *(int *)(param_1 + 0x18c) = *(int *)(param_1 + 0x18c) + -1;
    }
    *(uint *)(param_1 + 0x188) = (*(int *)(param_1 + 0x188) + 1U) % *(uint *)(param_1 + 0x184);
    return 0;
  }
  return 0;
}

```

## FUN_180063890 at `180063890`

Callers:
- none resolved

```c

/* WARNING: Removing unreachable block (ram,0x00018006391a) */

int FUN_180063890(longlong param_1,longlong param_2,undefined8 param_3,undefined8 param_4,
                 undefined4 param_5,undefined8 param_6)

{
  uint uVar1;
  uint uVar2;
  ulonglong uVar3;
  ulonglong uVar4;
  int iVar5;
  longlong lVar6;
  uint uVar7;
  uint uVar8;
  ulonglong uVar9;
  
  iVar5 = FUN_1800691a0(param_1);
  if (iVar5 < 0) {
    return iVar5;
  }
  uVar7 = *(uint *)(param_1 + 0x44);
  uVar1 = *(uint *)(param_1 + 0x9c);
  *(uint *)(param_1 + 0x14) = uVar1;
  *(undefined4 *)(param_1 + 0x40) = 0;
  *(undefined4 *)(param_1 + 100) = 100;
  if (uVar7 - 0x65 < 0xffffff9c) {
LAB_1800638ef:
    *(undefined4 *)(param_1 + 0x180) = 0;
    *(undefined8 *)(param_1 + 0x40) = 0x6400000000;
    *(undefined4 *)(param_1 + 0x68) = 0xff;
    *(undefined2 *)(param_1 + 0x6c) = 0;
    *(uint *)(param_1 + 0x9c) = uVar1 | 1;
    uVar8 = 0;
  }
  else {
    uVar8 = 2;
    if (uVar7 == 100) {
LAB_18006392b:
      *(uint *)(param_1 + 0x180) = uVar8;
    }
    else {
      uVar2 = *(uint *)(param_1 + 0x48);
      if (uVar2 <= uVar7 || 100 < uVar2) goto LAB_1800638ef;
      uVar8 = 3;
      if (uVar2 == 100) goto LAB_18006392b;
      uVar7 = *(uint *)(param_1 + 0x4c);
      if (uVar7 <= uVar2 || 100 < uVar7) goto LAB_1800638ef;
      uVar8 = 4;
      if (uVar7 == 100) goto LAB_18006392b;
      uVar2 = *(uint *)(param_1 + 0x50);
      if (uVar2 <= uVar7 || 100 < uVar2) goto LAB_1800638ef;
      uVar8 = 5;
      if (uVar2 == 100) goto LAB_18006392b;
      uVar7 = *(uint *)(param_1 + 0x54);
      if (uVar7 <= uVar2 || 100 < uVar7) goto LAB_1800638ef;
      uVar8 = 6;
      if (uVar7 == 100) goto LAB_18006392b;
      uVar2 = *(uint *)(param_1 + 0x58);
      if (uVar2 <= uVar7 || 100 < uVar2) goto LAB_1800638ef;
      uVar8 = 7;
      if (uVar2 == 100) goto LAB_18006392b;
      uVar7 = *(uint *)(param_1 + 0x5c);
      if (uVar7 <= uVar2 || 100 < uVar7) goto LAB_1800638ef;
      uVar8 = 8;
      if (uVar7 == 100) goto LAB_18006392b;
      uVar8 = *(uint *)(param_1 + 0x60);
      if (uVar8 <= uVar7 || 100 < uVar8) goto LAB_1800638ef;
      uVar8 = (uVar8 != 100) + 9;
      *(uint *)(param_1 + 0x180) = uVar8;
    }
    if ((uVar1 & 0x19) == 0) goto LAB_18006394e;
  }
  *(undefined8 *)(param_1 + 0x40) = 0x6400000000;
  *(undefined2 *)(param_1 + 0x68) = 0xffff;
  *(undefined1 *)(param_1 + 0x6a) = 0xff;
  *(undefined2 *)(param_1 + 0x6c) = 0;
  *(undefined1 *)(param_1 + 0x6e) = 0;
LAB_18006394e:
  uVar7 = 14000;
  if (*(uint *)(param_1 + 0x90) != 0) {
    uVar7 = *(uint *)(param_1 + 0x90);
  }
  uVar3 = 1000 / (ulonglong)*(uint *)(param_1 + 0x38);
  uVar4 = uVar7 / uVar3;
  uVar9 = (ulonglong)uVar8;
  if (uVar8 < (uint)uVar4) {
    uVar9 = uVar4;
  }
  *(int *)(param_1 + 0x184) = (int)uVar9;
  lVar6 = FUN_18014bdf0(0,(ulonglong)uVar7 % uVar3,uVar3,uVar9,param_5,param_6);
  *(ulonglong *)(param_1 + 0x178) = (lVar6 / 1800000) * 1800000 | 0x1f;
  if ((param_2 != 0) && ((*(byte *)(param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_2,1);
  }
  return iVar5;
}

```

## FUN_180063b30 at `180063b30`

Callers:
- none resolved

```c

undefined8 FUN_180063b30(longlong param_1,longlong param_2)

{
  undefined8 *puVar1;
  longlong lVar2;
  longlong *plVar3;
  longlong *plVar4;
  undefined8 uVar5;
  longlong *plVar6;
  uint uVar7;
  longlong *plVar8;
  bool bVar9;
  undefined1 auStack_58 [44];
  undefined4 local_2c;
  ulonglong local_28;
  
  local_28 = DAT_1801f4b40 ^ (ulonglong)auStack_58;
  if (param_2 == 0) {
    uVar5 = 0x80004005;
  }
  else {
    puVar1 = (undefined8 *)(param_1 + 400);
    plVar6 = *(longlong **)(param_1 + 400);
    plVar8 = (longlong *)*plVar6;
    if (plVar8 != plVar6) {
      do {
        FUN_180063cc0(plVar8[5]);
        plVar6 = (longlong *)plVar8[2];
        if (*(char *)(plVar8[2] + 0x19) == '\0') {
          do {
            plVar4 = plVar6;
            plVar6 = (longlong *)*plVar4;
          } while (*(char *)(*plVar4 + 0x19) == '\0');
        }
        else {
          do {
            plVar4 = (longlong *)plVar8[1];
            if (*(char *)((longlong)plVar4 + 0x19) != '\0') break;
            bVar9 = plVar8 == (longlong *)plVar4[2];
            plVar8 = plVar4;
          } while (bVar9);
        }
        plVar6 = (longlong *)*puVar1;
        plVar8 = plVar4;
      } while (plVar4 != plVar6);
    }
    uVar7 = (*(int *)(param_1 + 0x188) + 1U) % *(uint *)(param_1 + 0x38);
    uVar5 = 0;
    *(uint *)(param_1 + 0x188) = uVar7;
    if ((uVar7 == 0) && (plVar8 = (longlong *)*plVar6, plVar8 != plVar6)) {
      do {
        while( true ) {
          uVar5 = 0;
          lVar2 = plVar8[5];
          if ((lVar2 != 0) && (*(int *)(lVar2 + 0x18) == 0)) break;
          plVar4 = (longlong *)plVar8[2];
          if (*(char *)(plVar8[2] + 0x19) == '\0') {
            do {
              plVar8 = (longlong *)*plVar4;
              plVar3 = plVar4;
              plVar4 = plVar8;
            } while (*(char *)((longlong)plVar8 + 0x19) == '\0');
          }
          else {
            do {
              plVar3 = (longlong *)plVar8[1];
              if (*(char *)((longlong)plVar3 + 0x19) != '\0') break;
              bVar9 = plVar8 == (longlong *)plVar3[2];
              plVar8 = plVar3;
            } while (bVar9);
          }
          plVar8 = plVar3;
          if (plVar8 == plVar6) goto LAB_180063c9e;
        }
        local_2c = *(undefined4 *)(lVar2 + 8);
        FUN_180063d20(puVar1,&local_2c);
        FUN_180063880(lVar2);
        FUN_1800b9d98(lVar2,0x28);
        uVar5 = 0;
        plVar6 = (longlong *)*puVar1;
        plVar8 = (longlong *)*plVar6;
      } while (plVar8 != plVar6);
    }
  }
LAB_180063c9e:
  if ((local_28 ^ (ulonglong)auStack_58) != DAT_1801f4b40) {
                    /* WARNING: Subroutine does not return */
    FUN_1800b9f70();
  }
  return uVar5;
}

```

## FUN_180063e30 at `180063e30`

Callers:
- none resolved

```c

undefined8
FUN_180063e30(longlong *param_1,uint *param_2,uint param_3,int param_4,undefined4 param_5)

{
  longlong *plVar1;
  char cVar2;
  uint uVar3;
  longlong lVar4;
  longlong *plVar5;
  undefined8 *puVar6;
  void *pvVar7;
  longlong lVar8;
  undefined8 uVar9;
  ulonglong uVar10;
  bool bVar11;
  undefined4 local_38;
  undefined4 local_34;
  
  uVar9 = 0x80004005;
  if (param_3 != 0 && param_2 != (uint *)0x0) {
    uVar9 = 0;
    if ((param_4 != 0) && ((*(uint *)((longlong)param_1 + 0x14) & 0x1000) != 0)) {
      if (param_3 < 2) {
        uVar3 = *param_2;
        uVar9 = 0;
        if (uVar3 != 0xffffffff) {
          lVar8 = param_1[0x32];
          lVar4 = *(longlong *)(lVar8 + 8);
          cVar2 = *(char *)(lVar4 + 0x19);
          while (cVar2 == '\0') {
            bVar11 = *(uint *)(lVar4 + 0x20) < uVar3;
            if (!bVar11) {
              lVar8 = lVar4;
            }
            lVar4 = *(longlong *)(lVar4 + (ulonglong)bVar11 * 0x10);
            cVar2 = *(char *)(lVar4 + 0x19);
          }
          plVar1 = param_1 + 0x32;
          if ((*(char *)(lVar8 + 0x19) == '\0') && (*(uint *)(lVar8 + 0x20) <= uVar3)) {
            plVar5 = (longlong *)FUN_180064410(plVar1,param_2);
            lVar8 = *plVar5;
            puVar6 = (undefined8 *)FUN_180064410(plVar1,param_2);
            *puVar6 = 0;
            if (lVar8 != 0) {
              FUN_180063880(lVar8);
              FUN_1800b9d98(lVar8,0x28);
            }
          }
          if ((*(int *)((longlong)param_1 + 0x18c) == 0) &&
             ((*(byte *)((longlong)param_1 + 0x9c) & 0x19) != 0)) {
            if (((char)param_1[0xd] != '\0') ||
               ((*(char *)((longlong)param_1 + 0x69) != '\0' ||
                (*(char *)((longlong)param_1 + 0x6a) != '\0')))) {
              FUN_180069600(param_1,&local_38);
              *(undefined4 *)(param_1 + 0xd) = local_38;
            }
            if ((int)param_1[8] != 100) {
              if (((*(char *)((longlong)param_1 + 0x6c) != '\0') ||
                  (*(char *)((longlong)param_1 + 0x6d) != '\0')) ||
                 (*(char *)((longlong)param_1 + 0x6e) != '\0')) {
                FUN_180069600(param_1,&local_38);
                *(undefined4 *)((longlong)param_1 + 0x6c) = local_38;
              }
              if (*(int *)((longlong)param_1 + 0x44) != 100) {
                if ((((char)param_1[0xe] != '\0') || (*(char *)((longlong)param_1 + 0x71) != '\0'))
                   || (*(char *)((longlong)param_1 + 0x72) != '\0')) {
                  FUN_180069600(param_1,&local_38);
                  *(undefined4 *)(param_1 + 0xe) = local_38;
                }
                if ((int)param_1[9] != 100) {
                  if (((*(char *)((longlong)param_1 + 0x74) != '\0') ||
                      (*(char *)((longlong)param_1 + 0x75) != '\0')) ||
                     (*(char *)((longlong)param_1 + 0x76) != '\0')) {
                    FUN_180069600(param_1,&local_38);
                    *(undefined4 *)((longlong)param_1 + 0x74) = local_38;
                  }
                  if (*(int *)((longlong)param_1 + 0x4c) != 100) {
                    if ((((char)param_1[0xf] != '\0') ||
                        (*(char *)((longlong)param_1 + 0x79) != '\0')) ||
                       (*(char *)((longlong)param_1 + 0x7a) != '\0')) {
                      FUN_180069600(param_1,&local_38);
                      *(undefined4 *)(param_1 + 0xf) = local_38;
                    }
                    if ((int)param_1[10] != 100) {
                      if (((*(char *)((longlong)param_1 + 0x7c) != '\0') ||
                          (*(char *)((longlong)param_1 + 0x7d) != '\0')) ||
                         (*(char *)((longlong)param_1 + 0x7e) != '\0')) {
                        FUN_180069600(param_1,&local_38);
                        *(undefined4 *)((longlong)param_1 + 0x7c) = local_38;
                      }
                      if (*(int *)((longlong)param_1 + 0x54) != 100) {
                        if ((((char)param_1[0x10] != '\0') ||
                            (*(char *)((longlong)param_1 + 0x81) != '\0')) ||
                           (*(char *)((longlong)param_1 + 0x82) != '\0')) {
                          FUN_180069600(param_1,&local_38);
                          *(undefined4 *)(param_1 + 0x10) = local_38;
                        }
                        if ((int)param_1[0xb] != 100) {
                          if (((*(char *)((longlong)param_1 + 0x84) != '\0') ||
                              (*(char *)((longlong)param_1 + 0x85) != '\0')) ||
                             (*(char *)((longlong)param_1 + 0x86) != '\0')) {
                            FUN_180069600(param_1,&local_38);
                            *(undefined4 *)((longlong)param_1 + 0x84) = local_38;
                          }
                          if (*(int *)((longlong)param_1 + 0x5c) != 100) {
                            if ((((char)param_1[0x11] != '\0') ||
                                (*(char *)((longlong)param_1 + 0x89) != '\0')) ||
                               (*(char *)((longlong)param_1 + 0x8a) != '\0')) {
                              FUN_180069600(param_1,&local_38);
                              *(undefined4 *)(param_1 + 0x11) = local_38;
                            }
                            if (((int)param_1[0xc] != 100) &&
                               (((*(char *)((longlong)param_1 + 0x8c) != '\0' ||
                                 (*(char *)((longlong)param_1 + 0x8d) != '\0')) ||
                                (*(char *)((longlong)param_1 + 0x8e) != '\0')))) {
                              FUN_180069600(param_1,&local_38);
                              *(undefined4 *)((longlong)param_1 + 0x8c) = local_38;
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
          pvVar7 = operator_new(0x28);
          FUN_1800644f0(pvVar7,param_1);
          FUN_180064510(pvVar7,*param_2,*(undefined4 *)((longlong)param_1 + 0x184));
          puVar6 = (undefined8 *)FUN_180064410(plVar1,param_2);
          *puVar6 = pvVar7;
        }
      }
      else {
        *(undefined4 *)((longlong)param_1 + 0x18c) = 1;
        if ((*(byte *)((longlong)param_1 + 0x9c) & 0x19) != 0) {
          if ((((char)param_1[0xd] != '\0') || (*(char *)((longlong)param_1 + 0x69) != '\0')) ||
             (*(char *)((longlong)param_1 + 0x6a) != '\0')) {
            FUN_180069600(param_1,&local_34);
            *(undefined4 *)(param_1 + 0xd) = local_34;
          }
          if ((int)param_1[8] != 100) {
            if (((*(char *)((longlong)param_1 + 0x6c) != '\0') ||
                (*(char *)((longlong)param_1 + 0x6d) != '\0')) ||
               (*(char *)((longlong)param_1 + 0x6e) != '\0')) {
              FUN_180069600(param_1,&local_34);
              *(undefined4 *)((longlong)param_1 + 0x6c) = local_34;
            }
            if (*(int *)((longlong)param_1 + 0x44) != 100) {
              if ((((char)param_1[0xe] != '\0') || (*(char *)((longlong)param_1 + 0x71) != '\0')) ||
                 (*(char *)((longlong)param_1 + 0x72) != '\0')) {
                FUN_180069600(param_1,&local_34);
                *(undefined4 *)(param_1 + 0xe) = local_34;
              }
              if ((int)param_1[9] != 100) {
                if (((*(char *)((longlong)param_1 + 0x74) != '\0') ||
                    (*(char *)((longlong)param_1 + 0x75) != '\0')) ||
                   (*(char *)((longlong)param_1 + 0x76) != '\0')) {
                  FUN_180069600(param_1,&local_34);
                  *(undefined4 *)((longlong)param_1 + 0x74) = local_34;
                }
                if (*(int *)((longlong)param_1 + 0x4c) != 100) {
                  if ((((char)param_1[0xf] != '\0') || (*(char *)((longlong)param_1 + 0x79) != '\0')
                      ) || (*(char *)((longlong)param_1 + 0x7a) != '\0')) {
                    FUN_180069600(param_1,&local_34);
                    *(undefined4 *)(param_1 + 0xf) = local_34;
                  }
                  if ((int)param_1[10] != 100) {
                    if (((*(char *)((longlong)param_1 + 0x7c) != '\0') ||
                        (*(char *)((longlong)param_1 + 0x7d) != '\0')) ||
                       (*(char *)((longlong)param_1 + 0x7e) != '\0')) {
                      FUN_180069600(param_1,&local_34);
                      *(undefined4 *)((longlong)param_1 + 0x7c) = local_34;
                    }
                    if (*(int *)((longlong)param_1 + 0x54) != 100) {
                      if ((((char)param_1[0x10] != '\0') ||
                          (*(char *)((longlong)param_1 + 0x81) != '\0')) ||
                         (*(char *)((longlong)param_1 + 0x82) != '\0')) {
                        FUN_180069600(param_1,&local_34);
                        *(undefined4 *)(param_1 + 0x10) = local_34;
                      }
                      if ((int)param_1[0xb] != 100) {
                        if (((*(char *)((longlong)param_1 + 0x84) != '\0') ||
                            (*(char *)((longlong)param_1 + 0x85) != '\0')) ||
                           (*(char *)((longlong)param_1 + 0x86) != '\0')) {
                          FUN_180069600(param_1,&local_34);
                          *(undefined4 *)((longlong)param_1 + 0x84) = local_34;
                        }
                        if (*(int *)((longlong)param_1 + 0x5c) != 100) {
                          if ((((char)param_1[0x11] != '\0') ||
                              (*(char *)((longlong)param_1 + 0x89) != '\0')) ||
                             (*(char *)((longlong)param_1 + 0x8a) != '\0')) {
                            FUN_180069600(param_1,&local_34);
                            *(undefined4 *)(param_1 + 0x11) = local_34;
                          }
                          if (((int)param_1[0xc] != 100) &&
                             (((*(char *)((longlong)param_1 + 0x8c) != '\0' ||
                               (*(char *)((longlong)param_1 + 0x8d) != '\0')) ||
                              (*(char *)((longlong)param_1 + 0x8e) != '\0')))) {
                            FUN_180069600(param_1,&local_34);
                            *(undefined4 *)((longlong)param_1 + 0x8c) = local_34;
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        uVar10 = (ulonglong)param_3;
        do {
          if (*param_2 != 0xffffffff) {
            (**(code **)(*param_1 + 0x18))(param_1,param_2,1,param_4,param_5);
          }
          param_2 = param_2 + 1;
          uVar10 = uVar10 - 1;
        } while (uVar10 != 0);
        *(undefined4 *)((longlong)param_1 + 0x18c) = 0;
      }
    }
  }
  return uVar9;
}

```

## FUN_1800651c0 at `1800651c0`

Callers:
- none resolved

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_1800651c0(longlong param_1,longlong param_2)

{
  uint uVar1;
  ulonglong uVar2;
  undefined8 uVar3;
  undefined4 uVar4;
  undefined4 uVar5;
  undefined4 uVar6;
  ulonglong uVar7;
  longlong lVar8;
  void *pvVar9;
  uint uVar10;
  uint uVar11;
  int iVar12;
  ulonglong uVar13;
  
  uVar7 = FUN_1800691a0(param_1);
  if ((int)uVar7 < 0) {
    return uVar7;
  }
  uVar11 = *(uint *)(param_1 + 0x44);
  uVar10 = *(uint *)(param_1 + 0x9c);
  *(uint *)(param_1 + 0x14) = uVar10;
  *(undefined4 *)(param_1 + 0x40) = 0;
  *(undefined4 *)(param_1 + 100) = 100;
  if (uVar11 - 0x65 < 0xffffff9c) {
LAB_180065224:
    *(undefined4 *)(param_1 + 0x180) = 0;
    *(uint *)(param_1 + 0x9c) = uVar10 | 1;
  }
  else {
    iVar12 = 2;
    if (uVar11 != 100) {
      uVar1 = *(uint *)(param_1 + 0x48);
      if (uVar1 <= uVar11 || 100 < uVar1) goto LAB_180065224;
      iVar12 = 3;
      if (uVar1 != 100) {
        uVar11 = *(uint *)(param_1 + 0x4c);
        if (uVar11 <= uVar1 || 100 < uVar11) goto LAB_180065224;
        iVar12 = 4;
        if (uVar11 != 100) {
          uVar1 = *(uint *)(param_1 + 0x50);
          if (uVar1 <= uVar11 || 100 < uVar1) goto LAB_180065224;
          iVar12 = 5;
          if (uVar1 != 100) {
            uVar11 = *(uint *)(param_1 + 0x54);
            if (uVar11 <= uVar1 || 100 < uVar11) goto LAB_180065224;
            iVar12 = 6;
            if (uVar11 != 100) {
              uVar1 = *(uint *)(param_1 + 0x58);
              if (uVar1 <= uVar11 || 100 < uVar1) goto LAB_180065224;
              iVar12 = 7;
              if (uVar1 != 100) {
                uVar11 = *(uint *)(param_1 + 0x5c);
                if (uVar11 <= uVar1 || 100 < uVar11) goto LAB_180065224;
                iVar12 = 8;
                if (uVar11 != 100) {
                  uVar1 = *(uint *)(param_1 + 0x60);
                  if (uVar1 <= uVar11 || 100 < uVar1) goto LAB_180065224;
                  iVar12 = (uVar1 != 100) + 9;
                }
              }
            }
          }
        }
      }
    }
    *(int *)(param_1 + 0x180) = iVar12;
    if ((uVar10 & 1) == 0) goto LAB_18006529f;
  }
  uVar3 = _UNK_1801ac8e8;
  *(undefined8 *)(param_1 + 0x68) = _DAT_1801ac8e0;
  *(undefined8 *)(param_1 + 0x70) = uVar3;
  uVar6 = _UNK_1801ac8fc;
  uVar5 = _UNK_1801ac8f8;
  uVar4 = _UNK_1801ac8f4;
  *(undefined4 *)(param_1 + 0x40) = _DAT_1801ac8f0;
  *(undefined4 *)(param_1 + 0x44) = uVar4;
  *(undefined4 *)(param_1 + 0x48) = uVar5;
  *(undefined4 *)(param_1 + 0x4c) = uVar6;
  *(undefined4 *)(param_1 + 0x50) = 100;
  *(undefined4 *)(param_1 + 0x180) = 5;
  lVar8 = FUN_18014bdf0(0);
  *(ulonglong *)(param_1 + 0x178) = (lVar8 / 1800000) * 1800000 | 0x1f;
  uVar10 = *(uint *)(param_1 + 0x9c);
LAB_18006529f:
  if ((uVar10 & 0x18) != 0) {
    *(undefined8 *)(param_1 + 0x68) = 0;
    uVar3 = _UNK_1801ac908;
    *(undefined8 *)(param_1 + 0x40) = _DAT_1801ac900;
    *(undefined8 *)(param_1 + 0x48) = uVar3;
    *(undefined4 *)(param_1 + 0x70) = *(undefined4 *)(param_1 + 0xa0);
    *(undefined8 *)(param_1 + 0x74) = 0;
    uVar6 = _UNK_1801ac91c;
    uVar5 = _UNK_1801ac918;
    uVar4 = _UNK_1801ac914;
    *(undefined4 *)(param_1 + 0x50) = _DAT_1801ac910;
    *(undefined4 *)(param_1 + 0x54) = uVar4;
    *(undefined4 *)(param_1 + 0x58) = uVar5;
    *(undefined4 *)(param_1 + 0x5c) = uVar6;
    *(undefined4 *)(param_1 + 0x7c) = *(undefined4 *)(param_1 + 0xa0);
    *(undefined4 *)(param_1 + 0x60) = 100;
    *(undefined4 *)(param_1 + 0x80) = 0;
    *(undefined4 *)(param_1 + 0x180) = 9;
    lVar8 = FUN_18014bdf0(0);
    *(ulonglong *)(param_1 + 0x178) = (lVar8 / 1800000) * 1800000 | 0x1f;
  }
  *(uint *)(param_1 + 0x18c) = -(uint)(*(uint *)(param_1 + 0x94) == 0) | *(uint *)(param_1 + 0x94);
  uVar11 = 14000;
  if (*(uint *)(param_1 + 0x90) != 0) {
    uVar11 = *(uint *)(param_1 + 0x90);
  }
  uVar2 = (ulonglong)uVar11 / (1000 / (ulonglong)*(uint *)(param_1 + 0x38));
  uVar13 = (ulonglong)*(uint *)(param_1 + 0x180);
  if (*(uint *)(param_1 + 0x180) < (uint)uVar2) {
    uVar13 = uVar2;
  }
  *(int *)(param_1 + 0x184) = (int)uVar13;
  pvVar9 = operator_new(uVar13 * 4);
  *(void **)(param_1 + 0x198) = pvVar9;
  FUN_180194db0(pvVar9,0,uVar13 * 4);
  FUN_1800692e0(param_1,10,param_1 + 0x40,param_1 + 0x68,(int)uVar13,pvVar9);
  uVar11 = *(uint *)(param_1 + 0x14);
  if ((uVar11 & 0x1000) != 0 && param_2 != 0) {
    FUN_18004cde0(param_2,1);
    uVar11 = *(uint *)(param_1 + 0x14);
  }
  *(uint *)(param_1 + 400) = (uint)((uVar11 & 0x1000) == 0);
  return uVar7 & 0xffffffff;
}

```

## FUN_180065570 at `180065570`

Callers:
- none resolved

```c

undefined8 FUN_180065570(longlong param_1,longlong param_2)

{
  int iVar1;
  ulonglong uVar2;
  undefined8 uVar3;
  uint uVar4;
  undefined1 auStack_58 [32];
  undefined4 local_38;
  undefined8 local_30;
  undefined4 local_1c;
  ulonglong local_18;
  
  local_18 = DAT_1801f4b40 ^ (ulonglong)auStack_58;
  if (param_2 == 0) {
    uVar3 = 0x80004005;
  }
  else {
    uVar3 = 0;
    if (*(int *)(param_1 + 400) != 0) {
      if (*(int *)(param_1 + 0x18c) == 0) {
        *(undefined4 *)(param_1 + 400) = 0;
        uVar3 = 0;
      }
      else {
        if (*(int *)(param_1 + 0x1c) * *(int *)(param_1 + 0x18) != 0) {
          uVar2 = 0;
          do {
            *(undefined4 *)(param_2 + uVar2 * 4) =
                 *(undefined4 *)
                  (*(longlong *)(param_1 + 0x198) + (ulonglong)*(uint *)(param_1 + 0x188) * 4);
            uVar2 = uVar2 + 1;
          } while (uVar2 < (uint)(*(int *)(param_1 + 0x1c) * *(int *)(param_1 + 0x18)));
        }
        iVar1 = *(int *)(param_1 + 0x188);
        if (iVar1 == 0) {
          uVar4 = *(uint *)(param_1 + 0x9c);
          if ((uVar4 & 0x19) == 0) {
            iVar1 = 0;
          }
          else {
            if ((uVar4 & 8) != 0) {
              FUN_180069600(param_1,&local_1c);
              *(undefined4 *)(param_1 + 0x74) = local_1c;
              *(undefined4 *)(param_1 + 0x70) = local_1c;
              uVar4 = *(uint *)(param_1 + 0x9c);
            }
            if ((uVar4 & 0x10) != 0) {
              FUN_180069600(param_1,&local_1c);
              *(undefined4 *)(param_1 + 0x84) = local_1c;
              *(undefined4 *)(param_1 + 0x80) = local_1c;
              uVar4 = *(uint *)(param_1 + 0x9c);
            }
            if ((uVar4 & 1) != 0) {
              FUN_180069600(param_1,&local_1c);
              *(undefined4 *)(param_1 + 0x74) = local_1c;
              *(undefined4 *)(param_1 + 0x70) = local_1c;
            }
            local_30 = *(undefined8 *)(param_1 + 0x198);
            local_38 = *(undefined4 *)(param_1 + 0x184);
            FUN_1800692e0(param_1,10,param_1 + 0x40,param_1 + 0x68);
            iVar1 = *(int *)(param_1 + 0x188);
          }
        }
        uVar4 = (iVar1 + 1U) % *(uint *)(param_1 + 0x184);
        *(uint *)(param_1 + 0x188) = uVar4;
        uVar3 = 0;
        if ((uVar4 == 0) && (*(int *)(param_1 + 0x18c) != -1)) {
          *(int *)(param_1 + 0x18c) = *(int *)(param_1 + 0x18c) + -1;
          uVar3 = 0;
        }
      }
    }
  }
  if ((local_18 ^ (ulonglong)auStack_58) != DAT_1801f4b40) {
                    /* WARNING: Subroutine does not return */
    FUN_1800b9f70();
  }
  return uVar3;
}

```

## FUN_180065730 at `180065730`

Callers:
- none resolved

```c

undefined8
FUN_180065730(longlong param_1,undefined8 param_2,undefined8 param_3,int param_4,int param_5)

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
    if (*(int *)(param_1 + 400) == 1) goto LAB_18006577f;
  }
  *(undefined4 *)(param_1 + 400) = 1;
  uVar1 = *(undefined4 *)(param_1 + 0x94);
LAB_18006577f:
  *(undefined4 *)(param_1 + 0x18c) = uVar1;
  *(undefined4 *)(param_1 + 0x188) = 0;
  return 0;
}

```

## FUN_180065a80 at `180065a80`

Callers:
- none resolved

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_180065a80(longlong *param_1,undefined8 param_2,undefined4 param_3,undefined4 param_4,
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
  lVar2 = _UNK_1801ac9e8;
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
      param_1[8] = _DAT_1801ac9e0;
      param_1[9] = lVar2;
      uVar4 = _UNK_1801ac9fc;
      uVar3 = _UNK_1801ac9f8;
      uVar7 = _UNK_1801ac9f4;
      *(undefined4 *)(param_1 + 0xd) = _DAT_1801ac9f0;
      *(undefined4 *)((longlong)param_1 + 0x6c) = uVar7;
      *(undefined4 *)(param_1 + 0xe) = uVar3;
      *(undefined4 *)((longlong)param_1 + 0x74) = uVar4;
      uVar7 = 4;
    }
    *(undefined4 *)(param_1 + 0x30) = uVar7;
    uVar5 = *(uint *)((longlong)param_1 + 0x9c);
    *(uint *)((longlong)param_1 + 0x14) = uVar5;
    if ((uVar5 & 0x1000) != 0) {
      FUN_18004cde0(param_2,1);
      uVar5 = *(uint *)((longlong)param_1 + 0x14);
    }
    *(uint *)((longlong)param_1 + 0x194) = (uint)((uVar5 & 0x1000) == 0);
    (**(code **)(*param_1 + 0x48))(param_1,0,0,param_4,param_3,param_6);
    return uVar6 & 0xffffffff;
  }
  return uVar6;
}

```

## FUN_180065c90 at `180065c90`

Callers:
- none resolved

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

ulonglong FUN_180065c90(longlong param_1)

{
  int iVar1;
  uint uVar2;
  ulonglong uVar3;
  void *pvVar4;
  int iVar5;
  uint uVar6;
  int iVar7;
  ulonglong uVar8;
  uint uVar9;
  float fVar10;
  float fVar11;
  
  uVar3 = FUN_180069290();
  if (-1 < (int)uVar3) {
    uVar3 = uVar3 & 0xffffffff;
    if (*(longlong *)(param_1 + 0x198) != 0) {
      FUN_1800b9de0();
    }
    *(undefined8 *)(param_1 + 0x198) = 0;
    uVar9 = 10;
    if (10 < *(uint *)(param_1 + 0xa8)) {
      uVar9 = *(uint *)(param_1 + 0xa8);
    }
    *(uint *)(param_1 + 0xa8) = uVar9;
    uVar9 = *(uint *)(param_1 + 0x30);
    *(undefined4 *)(param_1 + 0x18c) = *(undefined4 *)(param_1 + 0x94);
    *(float *)(param_1 + 0x1b0) =
         (float)(((double)(*(int *)(param_1 + 0xb0) + 0x10e) * DAT_1801aca00) / _DAT_1801aca08);
    fVar10 = (float)FUN_180155ec0();
    iVar7 = (int)(fVar10 * (float)uVar9);
    uVar9 = *(uint *)(param_1 + 0x34);
    fVar10 = (float)FUN_18014de90();
    iVar1 = (int)(fVar10 * (float)uVar9);
    iVar5 = -iVar7;
    if (iVar5 < 0) {
      iVar5 = iVar7;
    }
    iVar7 = -iVar1;
    if (iVar7 < 0) {
      iVar7 = iVar1;
    }
    uVar9 = iVar7 + iVar5 + (uint)(iVar7 + iVar5 == 0);
    if ((*(byte *)(param_1 + 0x9d) & 0xc) != 0) {
      uVar9 = *(uint *)(param_1 + 0xa0);
      uVar2 = *(uint *)(param_1 + 0xa4);
      if (uVar2 == 0 || uVar9 == 0) {
        uVar9 = *(int *)(param_1 + 0x18) - 1;
        *(uint *)(param_1 + 0xa0) = uVar9;
        uVar2 = *(int *)(param_1 + 0x1c) - 1;
        *(uint *)(param_1 + 0xa4) = uVar2;
      }
      uVar9 = ((uVar2 & 0xffff) + ((uVar9 & 0xffff) - ((uVar2 >> 0x10) + (uVar9 >> 0x10)))) * 2;
    }
    if (*(int *)(param_1 + 0xac) == 0) {
      uVar8 = (ulonglong)*(uint *)(param_1 + 0x38);
      fVar10 = DAT_1801a9690 / (float)uVar8;
      fVar11 = (float)uVar9;
    }
    else {
      fVar11 = (float)uVar9;
      uVar8 = (ulonglong)*(uint *)(param_1 + 0x38);
      fVar10 = (((float)(uint)(*(int *)(param_1 + 0xac) << 2) / DAT_1801a9a70) * fVar11) /
               (float)uVar8;
    }
    *(float *)(param_1 + 400) = fVar10;
    *(int *)(param_1 + 0x1bc) = (int)(longlong)(fVar11 / fVar10);
    uVar6 = (uint)(longlong)
                  ((double)((longlong)(fVar11 / fVar10) & 0xffffffff) *
                  ((double)*(uint *)(param_1 + 0xa8) / DAT_1801aca10));
    *(uint *)(param_1 + 0x1a4) = uVar6;
    iVar5 = (int)((ulonglong)*(uint *)(param_1 + 0x98) / (1000 / uVar8));
    *(int *)(param_1 + 0x1a8) = iVar5;
    uVar2 = *(uint *)(param_1 + 0x180);
    if (uVar6 < uVar2) {
      *(uint *)(param_1 + 0x1a4) = uVar2;
      uVar6 = uVar2;
    }
    *(uint *)(param_1 + 0x1c0) = (uint)(uVar9 < uVar2);
    iVar5 = iVar5 + uVar6;
    *(int *)(param_1 + 0x184) = iVar5;
    uVar6 = iVar5 + uVar6;
    *(uint *)(param_1 + 0x1a0) = uVar6;
    pvVar4 = operator_new((ulonglong)uVar6 * 4);
    *(void **)(param_1 + 0x198) = pvVar4;
    FUN_180069590(param_1,0x64000000,uVar6,pvVar4);
    FUN_1800692e0(param_1,10,param_1 + 0x40,param_1 + 0x68,*(undefined4 *)(param_1 + 0x1a4),
                  *(undefined8 *)(param_1 + 0x198));
    *(ulonglong *)(param_1 + 0x1b4) =
         CONCAT44((uint)((ulonglong)*(undefined8 *)(param_1 + 0x30) >> 0x21),
                  (uint)*(undefined8 *)(param_1 + 0x30) >> 1);
  }
  return uVar3;
}

```

## FUN_1800666b0 at `1800666b0`

Callers:
- none resolved

```c

undefined8 FUN_1800666b0(longlong param_1,longlong param_2)

{
  int iVar1;
  uint uVar2;
  undefined8 uVar3;
  
  if (param_2 == 0) {
    return 0x80004005;
  }
  if (*(int *)(param_1 + 0x194) == 0) {
    return 0;
  }
  if (*(int *)(param_1 + 0x18c) == 0) {
    *(undefined4 *)(param_1 + 0x194) = 0;
    return 0;
  }
  iVar1 = *(int *)(param_1 + 0x188);
  if (((*(int *)(param_1 + 0x18c) == 1) && (iVar1 == 0)) && (*(int *)(param_1 + 0x1c0) == 0)) {
    *(undefined4 *)(param_1 + 0x184) = *(undefined4 *)(param_1 + 0x1a0);
  }
  if ((*(uint *)(param_1 + 0x9c) & 0x400) == 0) {
    if ((*(uint *)(param_1 + 0x9c) & 0x800) == 0) {
      FUN_180065f50(param_1,iVar1,*(undefined4 *)(param_1 + 0x24),*(undefined4 *)(param_1 + 0x2c),
                    *(undefined4 *)(param_1 + 0x20),*(undefined4 *)(param_1 + 0x28),param_2);
      iVar1 = *(int *)(param_1 + 0xac);
      goto joined_r0x000180066782;
    }
    uVar3 = 0;
  }
  else {
    uVar3 = 1;
  }
  FUN_180066160(param_1,iVar1,uVar3,param_2);
  iVar1 = *(int *)(param_1 + 0xac);
joined_r0x000180066782:
  if (((iVar1 != 0) &&
      (uVar2 = (*(int *)(param_1 + 0x188) + 1U) %
               (uint)(*(int *)(param_1 + 0x1a8) + *(int *)(param_1 + 0x1a4)),
      *(uint *)(param_1 + 0x188) = uVar2, uVar2 == 0)) && (*(int *)(param_1 + 0x18c) != -1)) {
    *(int *)(param_1 + 0x18c) = *(int *)(param_1 + 0x18c) + -1;
  }
  return 0;
}

```

## FUN_1800667d0 at `1800667d0`

Callers:
- none resolved

```c

undefined8
FUN_1800667d0(longlong param_1,undefined8 param_2,undefined8 param_3,int param_4,int param_5)

{
  undefined4 uVar1;
  
  if ((((param_5 == 0) || ((*(uint *)(param_1 + 0x14) & 0x1000) == 0)) ||
      (*(int *)(param_1 + 0x1ac) == param_4)) || (*(int *)(param_1 + 0x1ac) = param_4, param_4 == 0)
     ) {
    return 0;
  }
  if ((*(uint *)(param_1 + 0x14) & 4) == 0) {
    if (*(int *)(param_1 + 0x194) == 1) {
      return 0;
    }
  }
  else if (*(int *)(param_1 + 0x194) == 1) {
    *(undefined4 *)(param_1 + 0x188) = 0;
    uVar1 = 0;
    goto LAB_180066835;
  }
  *(undefined4 *)(param_1 + 0x194) = 1;
  *(undefined4 *)(param_1 + 0x188) = 0;
  uVar1 = *(undefined4 *)(param_1 + 0x94);
LAB_180066835:
  *(undefined4 *)(param_1 + 0x18c) = uVar1;
  *(int *)(param_1 + 0x184) = *(int *)(param_1 + 0x1a8) + *(int *)(param_1 + 0x1a4);
  return 0;
}

```
