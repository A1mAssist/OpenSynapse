# RzLightingEngineApi_v4.0.55.0.dll

Image base: `180000000`

## Matches
- `string` `ripple` at `18019cdb2`: `ripple`
- `string` `rzlightingapi` at `18019d7c1`: `RzLightingEngineAPI::RzLightingApi`
- `string` `rzlightingapi` at `18019d7e4`: `RzLightingEngineAPI::RzLightingApiNoReturn`
- `string` `starlight` at `1801a972f`: `Starlight   : Mode: %08X Duration: %dms MaxStar:%d `
- `string` `reactive` at `1801a9763`: `Reactive    : Mode: %08X Duration: %dms `
- `string` `ripple` at `1801a978c`: `Ripple      : Mode: %08X Duration: %dms Width:%d Speed:%d `
- `string` `wave` at `1801a97c7`: `Wave        : Mode: %08X Duration: %dms Width:%d Speed:%d Pause:%d Angle:%d `
- `string` `fire` at `1801a9814`: `Fire        : Mode: %08X Rate: %d `
- `string` `createlightingdevice` at `1801c59be`: `CreateLightingDevice`
- `string` `createlightingengine` at `1801c59d3`: `CreateLightingEngine`
- `string` `pollevents` at `1801c5a3b`: `PollEvents`
- `string` `rzlightingapi` at `1801c5a46`: `RzLightingApi`
- `string` `rzlightingapi` at `1801c5a54`: `RzLightingApiNoReturn`
- `string` `ripple` at `1801f4f30`: `.?AVCRippleEffect@@`
- `string` `starlight` at `1801f5270`: `.?AVCStarlightEffect@@`
- `string` `spectrumeffect` at `1801f52a0`: `.?AVCSpectrumEffect@@`
- `string` `fire` at `1801f5430`: `.?AVCFireEffect@@`
- `string` `reactive` at `1801f5460`: `.?AVCReactiveEffect@@`
- `string` `breathingeffect` at `1801f5490`: `.?AVCBreathingEffect@@`
- `string` `wave` at `1801f54c0`: `.?AVCWaveEffect@@`
- `symbol` `pollevents` at `180032d90`: `PollEvents`
- `symbol` `rzlightingapi` at `180033490`: `RzLightingApi`
- `symbol` `rzlightingapi` at `180033b60`: `RzLightingApiNoReturn`
- `symbol` `createlightingdevice` at `180045170`: `CreateLightingDevice`
- `symbol` `createlightingengine` at `180045230`: `CreateLightingEngine`
- `symbol` `starlight` at `1801a972f`: `s_Starlight_:_Mode:_%08X_Duration:_1801a972f`
- `symbol` `reactive` at `1801a9763`: `s_Reactive_:_Mode:_%08X_Duration:_%_1801a9763`
- `symbol` `ripple` at `1801a978c`: `s_Ripple_:_Mode:_%08X_Duration:_%d_1801a978c`
- `symbol` `wave` at `1801a97c7`: `s_Wave_:_Mode:_%08X_Duration:_%dms_1801a97c7`
- `symbol` `fire` at `1801a9814`: `s_Fire_:_Mode:_%08X_Rate:_%d_1801a9814`
- `symbol` `createlightingdevice` at `1801c59be`: `s_CreateLightingDevice_1801c59be`
- `symbol` `createlightingengine` at `1801c59d3`: `s_CreateLightingEngine_1801c59d3`
- `symbol` `pollevents` at `1801c5a3b`: `s_PollEvents_1801c5a3b`
- `symbol` `rzlightingapi` at `1801c5a46`: `s_RzLightingApi_1801c5a46`
- `symbol` `rzlightingapi` at `1801c5a54`: `s_RzLightingApiNoReturn_1801c5a54`

## RzLightingApi at `180033490`

Callers:
- none resolved

```c

undefined8 RzLightingApi(undefined8 param_1)

{
  bool bVar1;
  longlong *plVar2;
  char cVar3;
  byte *pbVar4;
  undefined8 uVar5;
  undefined8 uVar6;
  char *pcVar7;
  undefined1 local_68 [16];
  undefined1 local_58;
  byte *local_50;
  char *pcStack_48;
  undefined8 local_40;
  undefined8 local_38;
  
                    /* 0x33490  9  RzLightingApi */
  plVar2 = DAT_1801f7b08;
  local_38 = 0xfffffffffffffffe;
  local_58 = 0;
  local_50 = (byte *)0x0;
  pcStack_48 = (char *)0x0;
  local_40 = 0;
  if (DAT_1801f7b08 != (longlong *)0x0) {
    cVar3 = (**(code **)*DAT_1801f7b08)(DAT_1801f7b08);
    if (cVar3 != '\0') {
      pbVar4 = (byte *)(**(code **)(*plVar2 + 8))(plVar2,"anne.rzltengine");
      if ((pbVar4 != (byte *)0x0) && ((*pbVar4 & 5) != 0)) {
        local_58 = 1;
        pcVar7 = "RzLightingEngineAPI::RzLightingApi";
        pcStack_48 = "RzLightingEngineAPI::RzLightingApi";
        local_50 = pbVar4;
        uVar5 = (**(code **)(*plVar2 + 0x10))
                          (plVar2,0x58,pbVar4,"RzLightingEngineAPI::RzLightingApi",0,0,0,0,0,0,0);
        bVar1 = true;
        local_40 = uVar5;
        goto LAB_18003355f;
      }
    }
  }
  pbVar4 = (byte *)0x0;
  pcVar7 = (char *)0x0;
  uVar5 = 0;
  bVar1 = false;
LAB_18003355f:
  FUN_180033600(local_68,param_1);
  uVar6 = FUN_180033300(local_68);
  if (bVar1) {
    (**(code **)(*DAT_1801f7b08 + 0x18))(DAT_1801f7b08,pbVar4,pcVar7,uVar5);
  }
  return uVar6;
}

```

## RzLightingApiNoReturn at `180033b60`

Callers:
- none resolved

```c

void RzLightingApiNoReturn(undefined8 param_1)

{
  bool bVar1;
  longlong *plVar2;
  char cVar3;
  byte *pbVar4;
  undefined8 uVar5;
  char *pcVar6;
  undefined1 local_68 [8];
  undefined1 local_60 [8];
  undefined1 local_58;
  byte *local_50;
  char *pcStack_48;
  undefined8 local_40;
  undefined8 local_38;
  
                    /* 0x33b60  10  RzLightingApiNoReturn */
  plVar2 = DAT_1801f7b08;
  local_38 = 0xfffffffffffffffe;
  local_58 = 0;
  local_50 = (byte *)0x0;
  pcStack_48 = (char *)0x0;
  local_40 = 0;
  if (DAT_1801f7b08 != (longlong *)0x0) {
    cVar3 = (**(code **)*DAT_1801f7b08)(DAT_1801f7b08);
    if (cVar3 != '\0') {
      pbVar4 = (byte *)(**(code **)(*plVar2 + 8))(plVar2,"anne.rzltengine");
      if ((pbVar4 != (byte *)0x0) && ((*pbVar4 & 5) != 0)) {
        local_58 = 1;
        pcVar6 = "RzLightingEngineAPI::RzLightingApiNoReturn";
        pcStack_48 = "RzLightingEngineAPI::RzLightingApiNoReturn";
        local_50 = pbVar4;
        uVar5 = (**(code **)(*plVar2 + 0x10))
                          (plVar2,0x58,pbVar4,"RzLightingEngineAPI::RzLightingApiNoReturn",0,0,0,0,0
                           ,0,0);
        bVar1 = true;
        local_40 = uVar5;
        goto LAB_180033c2f;
      }
    }
  }
  pbVar4 = (byte *)0x0;
  pcVar6 = (char *)0x0;
  uVar5 = 0;
  bVar1 = false;
LAB_180033c2f:
  FUN_180033600(local_68,param_1);
  FUN_180009b60(local_60,local_68[0]);
  if (bVar1) {
    (**(code **)(*DAT_1801f7b08 + 0x18))(DAT_1801f7b08,pbVar4,pcVar6,uVar5);
  }
  return;
}

```

## FUN_180051000 at `180051000`

Callers:
- `FUN_180051200` at `180051200`

```c

void FUN_180051000(undefined8 param_1,undefined4 *param_2)

{
  char *pcVar1;
  undefined4 uVar2;
  double dVar3;
  
  switch(*param_2) {
  case 1:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Ripple      : Mode: %08X Duration: %dms Width:%d Speed:%d\n";
    break;
  case 2:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Wave        : Mode: %08X Duration: %dms Width:%d Speed:%d Pause:%d Angle:%d\n";
    break;
  case 3:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Reactive    : Mode: %08X Duration: %dms\n";
    break;
  case 4:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Spectrum    : Mode: %08X Duration: %dms\n";
    break;
  case 5:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Breath      : Mode: %08X Duration: %dms\n";
    break;
  case 6:
    uVar2 = param_2[2];
    dVar3 = (double)(ulonglong)(uint)param_2[1];
    pcVar1 = "Static      : Mode: %08X Color (0x%08X)\n";
    break;
  case 7:
    uVar2 = param_2[0x18];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "Starlight   : Mode: %08X Duration: %dms MaxStar:%d\n";
    break;
  case 8:
    uVar2 = param_2[0x16];
    dVar3 = (double)(ulonglong)(uint)param_2[0x18];
    pcVar1 = "Fire        : Mode: %08X Rate: %d\n";
    break;
  default:
    goto code_r0x0001800511ef;
  case 0xb:
    dVar3 = (double)(float)param_2[2];
    uVar2 = param_2[1];
    pcVar1 = "Ambience    : Fps: %d Blur: %f RECT:(%d,%d,%d,%d)\n";
    break;
  case 0xc:
    uVar2 = param_2[0x16];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "AudioVU     : Mode: %08X Decay: %d Boost: %f Auto: %d\n";
    break;
  case 0xd:
    uVar2 = param_2[0x17];
    dVar3 = (double)(ulonglong)(uint)param_2[0x15];
    pcVar1 = "ColorWheel  : Mode: %08X Speed: %d Center:(%d,%d)\n";
  }
  FUN_180045010(&DAT_180199a0c,0,pcVar1,uVar2,dVar3);
code_r0x0001800511ef:
  return;
}

```

## PollEvents at `180032d90`

Callers:
- none resolved

```c

longlong PollEvents(void)

{
  undefined8 *****pppppuVar1;
  undefined8 ****ppppuVar2;
  longlong lVar3;
  longlong lVar4;
  undefined8 *****pppppuVar5;
  ulonglong uVar6;
  ulonglong in_stack_ffffffffffffff78;
  undefined1 local_78 [8];
  undefined1 local_70 [8];
  undefined8 ****local_68;
  undefined8 ****ppppuStack_60;
  longlong local_58;
  ulonglong local_50;
  undefined8 local_40;
  
                    /* 0x32d90  8  PollEvents */
  local_40 = 0xfffffffffffffffe;
  local_68 = (undefined8 *****)0x0;
  ppppuStack_60 = (undefined8 *****)0x0;
  FUN_18002c950(local_78,&local_68);
  if (DAT_1801f7bf4 == '\x01') {
    local_68 = (undefined8 *****)0x0;
    ppppuStack_60 = (undefined8 *****)0x0;
    local_58 = 0;
    FUN_180040420(&DAT_1801f7c50,&local_68);
    ppppuVar2 = ppppuStack_60;
    pppppuVar1 = (undefined8 *****)ppppuStack_60;
    for (pppppuVar5 = (undefined8 *****)local_68; ppppuStack_60 = pppppuVar1,
        pppppuVar5 != (undefined8 *****)ppppuVar2; pppppuVar5 = pppppuVar5 + 2) {
      FUN_180033060(local_78,pppppuVar5);
      pppppuVar1 = (undefined8 *****)ppppuStack_60;
    }
    pppppuVar5 = (undefined8 *****)local_68;
    if ((undefined8 *****)local_68 != (undefined8 *****)0x0) {
      for (; pppppuVar5 != pppppuVar1; pppppuVar5 = pppppuVar5 + 2) {
        FUN_180009b60(pppppuVar5 + 1,*(undefined1 *)pppppuVar5);
      }
      uVar6 = local_58 - (longlong)local_68;
      pppppuVar5 = (undefined8 *****)local_68;
      if (0xfff < uVar6) {
        pppppuVar5 = (undefined8 *****)local_68[-1];
        if ((undefined1 *)0x1f < (undefined1 *)((longlong)local_68 + (-8 - (longlong)pppppuVar5)))
        goto LAB_180032fa3;
        uVar6 = uVar6 + 0x27;
      }
      FUN_1800b9d98(pppppuVar5,uVar6);
    }
  }
  in_stack_ffffffffffffff78 = in_stack_ffffffffffffff78 & 0xffffffffffffff00;
  FUN_180032a00(local_78,&local_68,0xffffffff,0x20,in_stack_ffffffffffffff78,0);
  lVar3 = local_58;
  if (0xf < local_50) {
    uVar6 = local_50 + 1;
    pppppuVar5 = (undefined8 *****)local_68;
    if (0xfff < uVar6) {
      pppppuVar5 = (undefined8 *****)local_68[-1];
      if ((undefined1 *)0x1f < (undefined1 *)((longlong)local_68 + (-8 - (longlong)pppppuVar5)))
      goto LAB_180032fa3;
      uVar6 = local_50 + 0x28;
    }
    FUN_1800b9d98(pppppuVar5,uVar6);
  }
  lVar4 = _malloc_base(lVar3 + 1);
  if (lVar4 != 0) {
    FUN_180032a00(local_78,&local_68,0xffffffff,0x20,in_stack_ffffffffffffff78 & 0xffffffffffffff00,
                  0);
    pppppuVar5 = &local_68;
    if (0xf < local_50) {
      pppppuVar5 = (undefined8 *****)local_68;
    }
    FUN_180156210(lVar4,lVar3 + 1,pppppuVar5);
    if (0xf < local_50) {
      uVar6 = local_50 + 1;
      pppppuVar5 = (undefined8 *****)local_68;
      if (0xfff < uVar6) {
        pppppuVar5 = (undefined8 *****)local_68[-1];
        if ((undefined1 *)0x1f < (undefined1 *)((longlong)local_68 + (-8 - (longlong)pppppuVar5))) {
LAB_180032fa3:
                    /* WARNING: Subroutine does not return */
          _invoke_watson((wchar_t *)0x0,(wchar_t *)0x0,(wchar_t *)0x0,0,0);
        }
        uVar6 = local_50 + 0x28;
      }
      FUN_1800b9d98(pppppuVar5,uVar6);
    }
  }
  FUN_180009b60(local_70,local_78[0]);
  return lVar4;
}

```

## FUN_180020300 at `180020300`

Callers:
- `FUN_18001fd90` at `18001fd90`

```c

ulonglong FUN_180020300(longlong param_1,char *param_2,undefined8 param_3)

{
  undefined1 uVar1;
  bool bVar2;
  uint uVar3;
  int iVar4;
  uint uVar5;
  longlong lVar6;
  undefined8 uVar7;
  ulonglong uVar8;
  ulonglong uVar9;
  undefined1 *puVar10;
  undefined8 *puVar11;
  char *pcVar12;
  char *pcVar13;
  undefined4 uVar14;
  undefined8 in_stack_fffffffffffffdf8;
  undefined1 local_1f8 [56];
  undefined8 local_1c0;
  undefined8 local_1b8;
  undefined8 uStack_1b0;
  undefined8 local_1a8;
  undefined8 uStack_1a0;
  undefined8 local_198;
  longlong alStack_170 [14];
  undefined **local_100 [12];
  undefined4 local_a0 [2];
  undefined8 local_98;
  undefined8 local_90;
  longlong local_88;
  undefined1 local_80 [8];
  undefined8 local_78;
  undefined8 *local_70;
  undefined8 local_68;
  undefined8 uStack_60;
  undefined8 local_58;
  ulonglong local_50;
  undefined8 local_38;
  
  uVar14 = (undefined4)((ulonglong)in_stack_fffffffffffffdf8 >> 0x20);
  local_38 = 0xfffffffffffffffe;
  local_90 = 0;
  local_70 = (undefined8 *)0x0;
  if (*param_2 == '\x01') {
    lVar6 = FUN_18002fe60(*(undefined8 *)(param_2 + 8),"config");
    if (lVar6 == **(longlong **)(param_2 + 8)) {
      if ((*param_2 != '\x01') ||
         (lVar6 = FUN_18002ff60(*(longlong **)(param_2 + 8),"fileName"),
         lVar6 == **(longlong **)(param_2 + 8))) goto LAB_1800205a9;
      uVar7 = FUN_180020210(param_2,"fileName");
      local_68 = (undefined **)0x0;
      uStack_60 = 0;
      local_58 = 0;
      local_50 = 0xf;
      FUN_18001ebf0(uVar7,&local_68);
      puVar11 = &local_68;
      if (0xf < local_50) {
        puVar11 = local_68;
      }
      uVar9 = CONCAT44(uVar14,1);
      FUN_180002340(&local_1b8,puVar11,1,0x40,uVar9);
      if (0xf < local_50) {
        uVar8 = local_50 + 1;
        puVar11 = local_68;
        if (0xfff < uVar8) {
          puVar11 = (undefined8 *)local_68[-1];
          if (0x1f < (ulonglong)((longlong)local_68 + (-8 - (longlong)puVar11))) goto LAB_18002098e;
          uVar8 = local_50 + 0x28;
        }
        FUN_1800b9d98(puVar11,uVar8);
      }
      if ((*(byte *)((longlong)&local_1a8 + (longlong)*(int *)(local_1b8 + 4)) & 6) == 0) {
        local_1c0 = 0;
        FUN_180002770(&local_68,&local_1b8,local_1f8,1,uVar9 & 0xffffffffffffff00);
        uVar3 = FUN_180002b20(&local_68,&local_70);
        uVar9 = (ulonglong)uVar3;
        FUN_180009b60(&uStack_60,(ulonglong)local_68 & 0xff);
        if ((int)uVar3 < 0) goto LAB_18002054a;
        lVar6 = FUN_180007e30(&uStack_1a0);
        bVar2 = true;
        if (lVar6 == 0) {
          lVar6 = (longlong)*(int *)(local_1b8 + 4);
          uVar3 = *(uint *)((longlong)&local_1a8 + lVar6 + 4);
          uVar5 = *(uint *)((longlong)&local_1a8 + lVar6) & 0x15 |
                  (uint)(*(longlong *)((longlong)alStack_170 + lVar6) == 0) << 2 | 2;
          *(uint *)((longlong)&local_1a8 + lVar6) = uVar5;
          uVar5 = uVar5 & uVar3;
          if (uVar5 != 0) {
            pcVar12 = "ios_base::failbit set";
            if ((uVar3 & 2) == 0) {
              pcVar12 = "ios_base::eofbit set";
            }
            pcVar13 = "ios_base::badbit set";
            if ((uVar5 & 4) == 0) {
              pcVar13 = pcVar12;
            }
            local_98 = FUN_18000df70();
            local_a0[0] = 1;
            FUN_18000e170(&local_68,local_a0,pcVar13);
            local_68 = std::ios_base::failure::vftable;
                    /* WARNING: Subroutine does not return */
            FUN_18010cda8(&local_68,&DAT_1801c7b58);
          }
        }
      }
      else {
        uVar7 = FUN_180020210(param_2,"fileName");
        local_68 = (undefined **)0x0;
        uStack_60 = 0;
        local_58 = 0;
        local_50 = 0xf;
        FUN_18001ebf0(uVar7,&local_68);
        puVar11 = &local_68;
        if (0xf < local_50) {
          puVar11 = local_68;
        }
        FUN_18003fb20(4,"Failed to open LedData: %s",puVar11);
        if (0xf < local_50) {
          uVar9 = local_50 + 1;
          puVar11 = local_68;
          if (0xfff < uVar9) {
            puVar11 = (undefined8 *)local_68[-1];
            if (0x1f < (ulonglong)((longlong)local_68 + (-8 - (longlong)puVar11))) {
LAB_18002098e:
                    /* WARNING: Subroutine does not return */
              _invoke_watson((wchar_t *)0x0,(wchar_t *)0x0,(wchar_t *)0x0,0,0);
            }
            uVar9 = local_50 + 0x28;
          }
          FUN_1800b9d98(puVar11,uVar9);
        }
        uVar9 = 0x80004005;
LAB_18002054a:
        bVar2 = false;
      }
      *(undefined ***)((longlong)&local_1b8 + (longlong)*(int *)(local_1b8 + 4)) =
           std::basic_fstream<char,std::char_traits<char>_>::vftable;
      *(int *)((longlong)&local_1c0 + (longlong)*(int *)(local_1b8 + 4) + 4) =
           *(int *)(local_1b8 + 4) + -0xb8;
      FUN_180007c80(&uStack_1a0);
      local_100[0] = std::ios_base::vftable;
      std::ios_base::_Ios_base_dtor((ios_base *)local_100);
      if (!bVar2) {
        return uVar9;
      }
      goto LAB_1800206f0;
    }
    uVar7 = FUN_180020210(param_2,"config");
    FUN_180003fe0(&local_1b8,uVar7);
    uVar3 = FUN_180002b20(&local_1b8,&local_70);
    uVar9 = (ulonglong)uVar3;
    FUN_180009b60(&uStack_1b0,local_1b8 & 0xff);
  }
  else {
LAB_1800205a9:
    local_1a8 = 0;
    uStack_1a0 = 0;
    local_1b8 = 0;
    uStack_1b0 = 0;
    local_198 = 0;
    uVar7 = FUN_180020210(param_2,&DAT_18019cb9e);
    local_68 = (undefined **)((ulonglong)local_68._4_4_ << 0x20);
    FUN_18000b730(uVar7,&local_68);
    local_1b8 = CONCAT62(local_1b8._2_6_,(undefined2)local_68);
    uVar7 = FUN_180020210(param_2,&DAT_18019cba2);
    local_68 = (undefined **)((ulonglong)local_68 & 0xffffffff00000000);
    FUN_18000b730(uVar7,&local_68);
    local_1b8._0_4_ = CONCAT22((undefined2)local_68,(undefined2)local_1b8);
    uVar9 = 0;
    uVar14 = 0;
    if (*param_2 == '\x01') {
      lVar6 = FUN_180030830(*(undefined8 *)(param_2 + 8),"edition");
      uVar9 = 0;
      if (lVar6 != **(longlong **)(param_2 + 8)) {
        uVar7 = FUN_180020210(param_2,"edition");
        local_68 = (undefined **)((ulonglong)local_68 & 0xffffffff00000000);
        FUN_18000b730(uVar7,&local_68);
        uVar9 = (ulonglong)local_68 & 0xffffffff;
      }
      uVar14 = 0;
      if ((*param_2 == '\x01') &&
         (lVar6 = FUN_18002fe60(*(undefined8 *)(param_2 + 8),"layout"),
         lVar6 != **(longlong **)(param_2 + 8))) {
        uVar7 = FUN_180020210(param_2,"layout");
        local_68 = (undefined **)((ulonglong)local_68 & 0xffffffff00000000);
        FUN_18000b730(uVar7,&local_68);
        uVar14 = (undefined4)local_68;
      }
    }
    uVar9 = FUN_180001dc0(&local_1b8,uVar9,uVar14,&local_70);
    uVar3 = (uint)uVar9;
  }
  if ((int)uVar3 < 0) {
    return uVar9;
  }
LAB_1800206f0:
  if (local_70 == (undefined8 *)0x0) {
    uVar9 = 0x80004005;
  }
  else {
    uVar9 = CreateLightingDevice(local_70,&local_90);
    if ((int)uVar9 < 0) {
      if (local_70 != (undefined8 *)0x0) {
        uVar9 = uVar9 & 0xffffffff;
        (**(code **)*local_70)(local_70,1);
      }
    }
    else {
      uVar9 = uVar9 & 0xffffffff;
      lVar6 = param_1 + 0x98;
      iVar4 = FUN_1800bd78c(lVar6);
      if (iVar4 != 0) {
                    /* WARNING: Subroutine does not return */
        FUN_1800bc30c(5);
      }
      local_88 = lVar6;
      if (*(int *)(param_1 + 0xe4) == 0x7fffffff) {
        *(undefined4 *)(param_1 + 0xe4) = 0x7ffffffe;
                    /* WARNING: Subroutine does not return */
        FUN_1800bc30c(6);
      }
      *(undefined4 *)(local_70 + 0xf) = *(undefined4 *)(param_1 + 0x40);
      local_80[0] = 0;
      local_78 = 0;
      FUN_18001f250(local_80);
      puVar10 = (undefined1 *)FUN_180002df0(param_3,"device_handle");
      uVar1 = *puVar10;
      *puVar10 = local_80[0];
      uVar7 = *(undefined8 *)(puVar10 + 8);
      *(undefined8 *)(puVar10 + 8) = local_78;
      local_80[0] = uVar1;
      local_78 = uVar7;
      FUN_180009b60(&local_78);
      local_68 = (undefined **)CONCAT44(local_68._4_4_,*(undefined4 *)(param_1 + 0x40));
      FUN_18002f3f0(param_1,&local_1b8,&local_68);
      puVar11 = *(undefined8 **)(local_1b8 + 0x18);
      *(undefined8 *)(local_1b8 + 0x18) = local_90;
      if (puVar11 != (undefined8 *)0x0) {
        (**(code **)*puVar11)(puVar11,1);
      }
      puVar11 = local_70;
      local_68 = (undefined **)CONCAT44(local_68._4_4_,*(undefined4 *)(param_1 + 0x40));
      FUN_18002f3f0(param_1,&local_1b8,&local_68);
      *(undefined8 **)(local_1b8 + 0x20) = puVar11;
      *(int *)(param_1 + 0x40) = *(int *)(param_1 + 0x40) + 1;
      FUN_1800bd7b8(local_88);
    }
  }
  return uVar9;
}

```

## CreateLightingDevice at `180045170`

Callers:
- `FUN_180020300` at `180020300`

```c

ulonglong CreateLightingDevice(undefined8 param_1,undefined8 *param_2)

{
  undefined8 *puVar1;
  ulonglong uVar2;
  
                    /* 0x45170  1  CreateLightingDevice */
  puVar1 = operator_new(0xe778);
  FUN_180047080(puVar1);
  uVar2 = (**(code **)(puVar1[1] + 0x10))(puVar1 + 1,param_1);
  if ((int)uVar2 < 0) {
    uVar2 = uVar2 & 0xffffffff;
    (**(code **)*puVar1)(puVar1,1);
    puVar1 = (undefined8 *)0x0;
  }
  *param_2 = puVar1;
  return uVar2;
}

```

## FUN_180020e10 at `180020e10`

Callers:
- `FUN_18001fd90` at `18001fd90`

```c

ulonglong FUN_180020e10(longlong param_1,char *param_2,undefined8 param_3)

{
  longlong *plVar1;
  int *piVar2;
  undefined1 uVar3;
  undefined8 *puVar4;
  int iVar5;
  undefined8 uVar6;
  ulonglong uVar7;
  undefined1 *puVar8;
  longlong lVar9;
  ulonglong uVar10;
  ulonglong uVar11;
  longlong lVar12;
  longlong local_88 [2];
  undefined8 local_78;
  longlong local_70;
  undefined8 local_68;
  undefined8 uStack_60;
  undefined8 local_58;
  ulonglong uStack_50;
  undefined1 local_48 [8];
  undefined8 local_40;
  undefined8 local_38;
  
  local_38 = 0xfffffffffffffffe;
  local_78 = 0;
  uVar6 = FUN_180020210(param_2,&DAT_18019cbb5);
  local_68 = (ulonglong)local_68._4_4_ << 0x20;
  FUN_18000b730(uVar6,&local_68);
  uVar7 = CreateLightingEngine(0,local_68 & 0xffffffff,&local_78);
  if (-1 < (int)uVar7) {
    uVar7 = uVar7 & 0xffffffff;
    lVar9 = param_1 + 0x98;
    iVar5 = FUN_1800bd78c(lVar9);
    if (iVar5 != 0) {
                    /* WARNING: Subroutine does not return */
      FUN_1800bc30c(5);
    }
    local_70 = lVar9;
    if (*(int *)(param_1 + 0xe4) == 0x7fffffff) {
      *(undefined4 *)(param_1 + 0xe4) = 0x7ffffffe;
                    /* WARNING: Subroutine does not return */
      FUN_1800bc30c(6);
    }
    local_48[0] = 0;
    local_40 = 0;
    FUN_18001f250(local_48,*(undefined4 *)(param_1 + 0x88));
    puVar8 = (undefined1 *)FUN_180002df0(param_3,"engine_handle");
    uVar3 = *puVar8;
    *puVar8 = local_48[0];
    uVar6 = *(undefined8 *)(puVar8 + 8);
    *(undefined8 *)(puVar8 + 8) = local_40;
    local_48[0] = uVar3;
    local_40 = uVar6;
    FUN_180009b60(&local_40);
    piVar2 = (int *)(param_1 + 0x88);
    if (*param_2 == '\x01') {
      lVar9 = FUN_1800302f0(*(undefined8 *)(param_2 + 8),&DAT_18019cbc7);
      if (lVar9 != **(longlong **)(param_2 + 8)) {
        uVar6 = FUN_180020210(param_2,&DAT_18019cbc7);
        local_68 = 0;
        uStack_60 = 0;
        local_58 = 0;
        uStack_50 = 0xf;
        FUN_18001ebf0(uVar6,&local_68);
        FUN_18002edd0(param_1 + 0x48,local_88,piVar2);
        plVar1 = (longlong *)(local_88[0] + 0x20);
        if (plVar1 == &local_68) {
          if (0xf < uStack_50) {
            uVar11 = uStack_50 + 1;
            uVar10 = local_68;
            if (0xfff < uVar11) {
              uVar10 = *(ulonglong *)(local_68 - 8);
              if (0x1f < (local_68 - 8) - uVar10) goto LAB_180021091;
              uVar11 = uStack_50 + 0x28;
            }
            FUN_1800b9d98(uVar10,uVar11);
          }
        }
        else {
          uVar11 = *(ulonglong *)(local_88[0] + 0x38);
          if (0xf < uVar11) {
            lVar9 = *plVar1;
            uVar10 = uVar11 + 1;
            lVar12 = lVar9;
            if (0xfff < uVar10) {
              lVar12 = *(longlong *)(lVar9 + -8);
              if (0x1f < (ulonglong)((lVar9 + -8) - lVar12)) {
LAB_180021091:
                    /* WARNING: Subroutine does not return */
                _invoke_watson((wchar_t *)0x0,(wchar_t *)0x0,(wchar_t *)0x0,0,0);
              }
              uVar10 = uVar11 + 0x28;
            }
            FUN_1800b9d98(lVar12,uVar10);
          }
          *(undefined8 *)(local_88[0] + 0x30) = local_58;
          *(ulonglong *)(local_88[0] + 0x38) = uStack_50;
          *(undefined4 *)plVar1 = (undefined4)local_68;
          *(uint *)(local_88[0] + 0x24) = local_68._4_4_;
          *(undefined4 *)(local_88[0] + 0x28) = (undefined4)uStack_60;
          *(undefined4 *)(local_88[0] + 0x2c) = uStack_60._4_4_;
        }
      }
    }
    FUN_18002edd0(param_1 + 0x48,&local_68,piVar2);
    puVar4 = *(undefined8 **)(local_68 + 0x18);
    *(undefined8 *)(local_68 + 0x18) = local_78;
    if (puVar4 != (undefined8 *)0x0) {
      (**(code **)*puVar4)(puVar4,1);
    }
    FUN_18002edd0(param_1 + 0x48,&local_68,piVar2);
    *(undefined4 *)(local_68 + 0x80) = 1;
    *piVar2 = *piVar2 + 1;
    FUN_1800bd7b8(local_70);
  }
  return uVar7;
}

```

## FUN_180041ff0 at `180041ff0`

Callers:
- `FUN_180043240` at `180043240`

```c

undefined8 * FUN_180041ff0(undefined8 *param_1)

{
  undefined8 *puVar1;
  int iVar2;
  undefined8 local_28;
  undefined8 *local_20;
  undefined8 local_18;
  
  local_18 = 0xfffffffffffffffe;
  FUN_180041940();
  *param_1 = IdentifyEngineImpl::vftable;
  param_1[0x15] = IdentifyEngineImpl::vftable;
  param_1[0x19] = 0;
  param_1[0x1a] = 0;
  param_1[0x1b] = 0;
  param_1[0x1c] = 0;
  param_1[0x1d] = 0;
  param_1[0x1e] = 0;
  param_1[0x1f] = 0;
  param_1[0x20] = 0;
  param_1[0x16] = 0;
  param_1[0x17] = 0;
  *(undefined4 *)(param_1 + 0x21) = 0xffffffff;
  *(undefined4 *)(param_1 + 0x18) = 2;
  param_1[0x2d] = 0;
  *(undefined8 *)((longlong)param_1 + 0x10c) = 0;
  *(undefined8 *)((longlong)param_1 + 0x114) = 0;
  *(undefined8 *)((longlong)param_1 + 0x11c) = 0;
  *(undefined8 *)((longlong)param_1 + 0x124) = 0;
  *(undefined4 *)((longlong)param_1 + 300) = 0;
  local_28 = 0;
  iVar2 = CreateLightingEngine(0,0x19,&local_28);
  if (-1 < iVar2) {
    puVar1 = (undefined8 *)param_1[0x25];
    param_1[0x25] = local_28;
    if (puVar1 != (undefined8 *)0x0) {
      (**(code **)*puVar1)(puVar1,1);
    }
  }
  local_20 = param_1;
  FUN_180041a20(param_1,0xffffffff);
  return local_20;
}

```

## FUN_180042cd0 at `180042cd0`

Callers:
- none resolved

```c

undefined8 FUN_180042cd0(longlong param_1)

{
  longlong *plVar1;
  undefined8 *puVar2;
  int iVar3;
  undefined1 auStack_48 [32];
  undefined4 local_28;
  undefined8 local_18;
  ulonglong local_10;
  
  local_10 = DAT_1801f4b40 ^ (ulonglong)auStack_48;
  FUN_180041be0(param_1 + -0xa8);
  plVar1 = *(longlong **)(param_1 + 0x80);
  if (plVar1 != (longlong *)0x0) {
    local_28 = 1;
    (**(code **)(*plVar1 + 0x80))(plVar1,0,0,0);
    local_18 = 0;
    iVar3 = CreateLightingEngine(0,0x19,&local_18);
    if (iVar3 < 0) {
      puVar2 = *(undefined8 **)(param_1 + 0x80);
      *(undefined8 *)(param_1 + 0x80) = 0;
    }
    else {
      puVar2 = *(undefined8 **)(param_1 + 0x80);
      *(undefined8 *)(param_1 + 0x80) = local_18;
    }
    if (puVar2 != (undefined8 *)0x0) {
      (**(code **)*puVar2)(puVar2,1);
    }
  }
  if ((local_10 ^ (ulonglong)auStack_48) == DAT_1801f4b40) {
    return 0;
  }
                    /* WARNING: Subroutine does not return */
  FUN_1800b9f70();
}

```

## FUN_180001dc0 at `180001dc0`

Callers:
- `FUN_180020300` at `180020300`

```c

int FUN_180001dc0(longlong param_1,undefined1 param_2,undefined1 param_3,undefined8 *param_4)

{
  longlong *plVar1;
  BOOL BVar2;
  DWORD DVar3;
  uint uVar4;
  int iVar5;
  uint uVar6;
  longlong lVar7;
  char *pcVar8;
  char *pcVar9;
  WCHAR local_6e8 [264];
  undefined1 local_4d8 [272];
  undefined1 local_3c8 [272];
  undefined1 local_2b8 [56];
  undefined8 local_280;
  undefined **local_278;
  undefined1 local_270 [16];
  undefined1 local_260 [160];
  undefined **local_1c0 [11];
  int iStack_164;
  longlong local_160 [2];
  uint auStack_150 [2];
  undefined1 local_148 [48];
  longlong alStack_118 [14];
  undefined **local_a8 [12];
  undefined4 local_48 [2];
  undefined8 local_40;
  HMODULE local_38;
  longlong *local_30;
  undefined8 local_28;
  
  local_28 = 0xfffffffffffffffe;
  *param_4 = 0;
  FUN_1800022b0(local_4d8,"PID%04x_EID%02x_%02x",*(undefined2 *)(param_1 + 2),param_2,param_3);
  local_38 = (HMODULE)0x0;
  FUN_180194db0(local_6e8,0,0x208);
  BVar2 = GetModuleHandleExW(6,(LPCWSTR)CreateLightingEngine,&local_38);
  if (BVar2 == 0) {
    DVar3 = GetLastError();
    FUN_18003fb20(4,"GetModuleHandle failed, error = %d\n",DVar3);
  }
  GetModuleFileNameW(local_38,local_6e8,0x104);
  PathRemoveFileSpecW(local_6e8);
  FUN_1800022b0(local_3c8,"%S\\LedData\\%s.json",local_6e8,local_4d8);
  FUN_18003fb20(4,"LedData: %s",local_3c8);
  uVar6 = 0;
  FUN_180002340(local_160,local_3c8,1,0x40,1);
  uVar4 = *(uint *)((longlong)auStack_150 + (longlong)*(int *)(local_160[0] + 4));
  if ((uVar4 & 6) != 0) {
    FUN_1800022b0(local_3c8,"C:\\Windows\\System32\\LedData\\%s.json",local_4d8);
    uVar6 = 0;
    FUN_180002340(&local_278,local_3c8,1,0x40,1);
    FUN_18000ebf0(local_160,&local_278);
    *(undefined ***)(local_270 + (longlong)*(int *)((longlong)local_278 + 4) + -8) =
         std::basic_fstream<char,std::char_traits<char>_>::vftable;
    *(int *)((longlong)&local_280 + (longlong)*(int *)((longlong)local_278 + 4) + 4) =
         *(int *)((longlong)local_278 + 4) + -0xb8;
    FUN_180007c80(local_260);
    local_1c0[0] = std::ios_base::vftable;
    std::ios_base::_Ios_base_dtor((ios_base *)local_1c0);
    uVar4 = *(uint *)((longlong)auStack_150 + (longlong)*(int *)(local_160[0] + 4));
    if ((uVar4 & 6) != 0) {
      iVar5 = -0x7fffbffb;
      FUN_18003fb20(4,"Failed to open LedData: %s",local_3c8);
      goto LAB_1800020ad;
    }
  }
  iVar5 = -0x7fffbffb;
  if ((uVar4 & 4) == 0) {
    local_280 = 0;
    FUN_180002770(&local_278,local_160,local_2b8,1,uVar6 & 0xffffff00);
    local_30 = operator_new(0x4be0);
    FUN_180002960(local_30);
    plVar1 = local_30;
    iVar5 = (**(code **)(*local_30 + 0x58))(local_30,&local_278);
    if (-1 < iVar5) {
      *param_4 = plVar1;
    }
    FUN_180009b60(local_270,(ulonglong)local_278 & 0xff);
  }
  lVar7 = FUN_180007e30(local_148);
  if (lVar7 == 0) {
    lVar7 = (longlong)*(int *)(local_160[0] + 4);
    uVar4 = *(uint *)((longlong)auStack_150 + lVar7 + 4);
    uVar6 = *(uint *)((longlong)auStack_150 + lVar7) & 0x15 |
            (uint)(*(longlong *)((longlong)alStack_118 + lVar7) == 0) << 2 | 2;
    *(uint *)((longlong)auStack_150 + lVar7) = uVar6;
    uVar6 = uVar6 & uVar4;
    if (uVar6 != 0) {
      pcVar8 = "ios_base::failbit set";
      if ((uVar4 & 2) == 0) {
        pcVar8 = "ios_base::eofbit set";
      }
      pcVar9 = "ios_base::badbit set";
      if ((uVar6 & 4) == 0) {
        pcVar9 = pcVar8;
      }
      local_40 = FUN_18000df70();
      local_48[0] = 1;
      FUN_18000e170(&local_278,local_48,pcVar9);
      local_278 = std::ios_base::failure::vftable;
                    /* WARNING: Subroutine does not return */
      FUN_18010cda8(&local_278,&DAT_1801c7b58);
    }
  }
LAB_1800020ad:
  *(undefined ***)((longlong)local_160 + (longlong)*(int *)(local_160[0] + 4)) =
       std::basic_fstream<char,std::char_traits<char>_>::vftable;
  *(int *)((longlong)&iStack_164 + (longlong)*(int *)(local_160[0] + 4)) =
       *(int *)(local_160[0] + 4) + -0xb8;
  FUN_180007c80(local_148);
  local_a8[0] = std::ios_base::vftable;
  std::ios_base::_Ios_base_dtor((ios_base *)local_a8);
  return iVar5;
}

```

## CreateLightingEngine at `180045230`

Callers:
- `FUN_180020e10` at `180020e10`
- `FUN_180041ff0` at `180041ff0`
- `FUN_180042cd0` at `180042cd0`
- `FUN_180001dc0` at `180001dc0`

```c

ulonglong CreateLightingEngine(undefined8 param_1,undefined4 param_2,undefined8 *param_3)

{
  undefined8 *puVar1;
  ulonglong uVar2;
  
                    /* 0x45230  2  CreateLightingEngine */
  puVar1 = operator_new(0x160);
  FUN_18004b2e0(puVar1);
  uVar2 = FUN_18004b6c0(puVar1,param_2);
  if ((int)uVar2 < 0) {
    uVar2 = uVar2 & 0xffffffff;
    (**(code **)*puVar1)(puVar1,1);
    puVar1 = (undefined8 *)0x0;
  }
  *param_3 = puVar1;
  return uVar2;
}

```
