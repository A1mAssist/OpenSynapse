## vftable `1801ac5a8`

- `1800601bc` in `FUN_1800601a0`
- `1800601c3` in `FUN_1800601a0`
- `18006028c` in `FUN_180060280`
- `180060293` in `FUN_180060280`

## vftable `1801ac698`

- `180061dcd` in `FUN_180061dc0`
- `180061dd4` in `FUN_180061dc0`
- `180061e26` in `FUN_180061e10`
- `180061e2d` in `FUN_180061e10`

## vftable `1801ac758`

- `18006242d` in `FUN_180062420`
- `180062434` in `FUN_180062420`
- `180062465` in `FUN_180062460`
- `18006246c` in `FUN_180062460`

## vftable `1801ac928`

- `1800650fd` in `FUN_1800650f0`
- `180065104` in `FUN_1800650f0`
- `180065156` in `FUN_180065140`
- `18006515d` in `FUN_180065140`

## vftable `1801aca28`

- `1800659bd` in `FUN_1800659b0`
- `1800659c4` in `FUN_1800659b0`
- `180065a16` in `FUN_180065a00`
- `180065a1d` in `FUN_180065a00`

## FUN_1800601a0 at `1800601a0`

```c

undefined8 *
FUN_1800601a0(undefined8 *param_1,undefined8 param_2,undefined8 param_3,undefined8 param_4)

{
  void *pvVar1;
  undefined8 uVar2;
  
  uVar2 = 0xfffffffffffffffe;
  FUN_180069140();
  *param_1 = CStarlightEffect::vftable;
  param_1[0x33] = 0;
  param_1[0x34] = 0;
  pvVar1 = operator_new(0x30);
  *(void **)pvVar1 = pvVar1;
  *(void **)((longlong)pvVar1 + 8) = pvVar1;
  *(void **)((longlong)pvVar1 + 0x10) = pvVar1;
  *(undefined2 *)((longlong)pvVar1 + 0x18) = 0x101;
  param_1[0x33] = pvVar1;
  FUN_180061470(param_1 + 0x33,param_1 + 0x33,pvVar1,param_4,param_1,uVar2);
  *(void **)((longlong)pvVar1 + 8) = pvVar1;
  *(void **)pvVar1 = pvVar1;
  *(void **)((longlong)pvVar1 + 0x10) = pvVar1;
  param_1[0x34] = 0;
  param_1[0x30] = 0;
  *(undefined4 *)(param_1 + 0x31) = 0;
  return param_1;
}


```

## FUN_180060280 at `180060280`

```c

void FUN_180060280(undefined8 *param_1)

{
  undefined8 *puVar1;
  longlong lVar2;
  longlong *plVar3;
  longlong *plVar4;
  longlong *plVar5;
  bool bVar6;
  
  *param_1 = CStarlightEffect::vftable;
  puVar1 = param_1 + 0x33;
  plVar4 = (longlong *)param_1[0x33];
  plVar5 = (longlong *)*plVar4;
  if ((longlong *)*plVar4 != plVar4) {
    do {
      lVar2 = plVar5[5];
      if (lVar2 != 0) {
        FUN_180060380(lVar2);
        FUN_1800b9d98(lVar2,0x28);
      }
      plVar4 = (longlong *)plVar5[2];
      if (*(char *)(plVar5[2] + 0x19) == '\0') {
        do {
          plVar3 = plVar4;
          plVar4 = (longlong *)*plVar3;
        } while (*(char *)(*plVar3 + 0x19) == '\0');
      }
      else {
        do {
          plVar3 = (longlong *)plVar5[1];
          if (*(char *)((longlong)plVar3 + 0x19) != '\0') break;
          bVar6 = plVar5 == (longlong *)plVar3[2];
          plVar5 = plVar3;
        } while (bVar6);
      }
      plVar4 = (longlong *)*puVar1;
      plVar5 = plVar3;
    } while (plVar3 != plVar4);
  }
  FUN_180061470(puVar1,puVar1,plVar4[1]);
  plVar4[1] = (longlong)plVar4;
  *plVar4 = (longlong)plVar4;
  plVar4[2] = (longlong)plVar4;
  param_1[0x34] = 0;
  FUN_180061470(puVar1,puVar1,*(undefined8 *)(param_1[0x33] + 8));
  FUN_1800b9d98(param_1[0x33],0x30);
  FUN_180069190(param_1);
  return;
}


```

## FUN_180061dc0 at `180061dc0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_180061dc0(undefined8 *param_1)

{
  undefined8 uVar1;
  
  FUN_180069140();
  *param_1 = CSpectrumEffect::vftable;
  param_1[0x33] = 0;
  uVar1 = _UNK_1801ac688;
  param_1[0x30] = _DAT_1801ac680;
  param_1[0x31] = uVar1;
  *(undefined4 *)(param_1 + 0x32) = 1;
  return param_1;
}


```

## FUN_180061e10 at `180061e10`

```c

void FUN_180061e10(undefined8 *param_1,undefined8 param_2,undefined8 param_3,undefined8 param_4)

{
  *param_1 = CSpectrumEffect::vftable;
  if ((param_1[1] != 0) && ((*(byte *)((longlong)param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_1[1],0,param_3,param_4,0xfffffffffffffffe);
  }
  if (param_1[0x33] != 0) {
    FUN_1800b9de0();
  }
  FUN_180069190(param_1);
  return;
}


```

## FUN_180062420 at `180062420`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_180062420(undefined8 *param_1)

{
  undefined8 uVar1;
  
  FUN_180069140();
  *param_1 = CFireEffect::vftable;
  uVar1 = _UNK_1801ac688;
  param_1[0x30] = _DAT_1801ac680;
  param_1[0x31] = uVar1;
  param_1[0x32] = 0;
  return param_1;
}


```

## FUN_180062460 at `180062460`

```c

void FUN_180062460(undefined8 *param_1)

{
  *param_1 = CFireEffect::vftable;
  if (param_1[0x32] != 0) {
    FUN_1800b9de0(param_1[0x32]);
  }
  param_1[0x32] = 0;
  FUN_180069190();
  return;
}


```

## FUN_1800650f0 at `1800650f0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_1800650f0(undefined8 *param_1)

{
  undefined8 uVar1;
  
  FUN_180069140();
  *param_1 = CBreathingEffect::vftable;
  param_1[0x33] = 0;
  uVar1 = _UNK_1801ac688;
  param_1[0x30] = _DAT_1801ac680;
  param_1[0x31] = uVar1;
  param_1[0x32] = 1;
  return param_1;
}


```

## FUN_180065140 at `180065140`

```c

void FUN_180065140(undefined8 *param_1,undefined8 param_2,undefined8 param_3,undefined8 param_4)

{
  *param_1 = CBreathingEffect::vftable;
  if ((param_1[1] != 0) && ((*(byte *)((longlong)param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_1[1],0,param_3,param_4,0xfffffffffffffffe);
  }
  if (param_1[0x33] != 0) {
    FUN_1800b9de0();
  }
  FUN_180069190(param_1);
  return;
}


```

## FUN_1800659b0 at `1800659b0`

```c

/* WARNING: Globals starting with '_' overlap smaller symbols at the same address */

undefined8 * FUN_1800659b0(undefined8 *param_1)

{
  undefined8 uVar1;
  
  FUN_180069140();
  *param_1 = CWaveEffect::vftable;
  param_1[0x33] = 0;
  param_1[0x34] = 0;
  param_1[0x35] = 0;
  uVar1 = _UNK_1801ac688;
  param_1[0x30] = _DAT_1801ac680;
  param_1[0x31] = uVar1;
  *(undefined4 *)((longlong)param_1 + 0x194) = 1;
  return param_1;
}


```

## FUN_180065a00 at `180065a00`

```c

void FUN_180065a00(undefined8 *param_1,undefined8 param_2,undefined8 param_3,undefined8 param_4)

{
  *param_1 = CWaveEffect::vftable;
  if ((param_1[1] != 0) && ((*(byte *)((longlong)param_1 + 0x15) & 0x10) != 0)) {
    FUN_18004cde0(param_1[1],0,param_3,param_4,0xfffffffffffffffe);
  }
  if (param_1[0x33] != 0) {
    FUN_1800b9de0();
  }
  FUN_180069190(param_1);
  return;
}


```

