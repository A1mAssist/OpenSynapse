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

