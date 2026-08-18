// Ghidra headless script: dump only mapping_engine functions relevant to input I/O.
// @category OpenSynapse

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

import java.io.File;
import java.io.PrintWriter;
import java.util.LinkedHashMap;
import java.util.Map;

public class DumpMappingIo extends GhidraScript {
    private final Map<Address, String> targets = new LinkedHashMap<>();

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("expected output path");
        }

        addRva(0x26e40, "export addUsbDevice");
        addRva(0x264d0, "export addUsbDeviceWithoutFilterDriver");
        addRva(0x371a0, "export registerInputNotification");
        addRva(0x38220, "export setInputNotificationCallback");
        addRva(0x32de0, "export enableRazerKeyInputRedirect");
        addRva(0x33e60, "export setInputRedirectCallback");
        addRva(0x2ba60, "export isUnsupportedMappingRegistered");
        addRva(0x2c270, "export registerUnsupportedMapping");
        addRva(0x2ca80, "export unregisterUnsupportedMapping");
        addRva(0x2d300, "export setUnsupportedMappingCallback");
        addRva(0x10dc70, "DriverImpl constructor");
        addRva(0x10dec0, "DriverImpl setup");
        addRva(0x10eaea, "DriverImpl DeviceIoControl helper");
        addRva(0x112027, "parse driver keyboard input");
        addRva(0x1120a9, "parse driver mouse or Razer input");
        addRva(0x112140, "DriverImpl OnIOCompleted");
        addRva(0x114380, "DriverThread Razer-key redirect entry");
        addRva(0x116960, "DriverThread Razer-key redirect task");
        addRva(0x1169b0, "DriverThread Razer-key redirect task helper");
        addRva(0x68b90, "MappingEngineInputDevice callback candidate A");
        addRva(0xa8cb0, "MappingEngineInputDevice callback candidate B");
        addRva(0x69090, "MappingEngineInputDevice callback candidate C");
        addRva(0x69990, "MappingEngineInputDevice callback candidate D");
        addRva(0x120cb8, "hardware event async ReadFile helper");
        addRva(0x1221da, "hardware report buffer conversion");
        addRva(0x117c3e, "filter-driver endpoint enumeration");
        addRva(0x116ffc, "filter-driver endpoint result parser");
        addRva(0x117336, "filter-driver endpoint enumeration implementation");
        addReferenceCallers(currentProgram.getImageBase().add(0x10dc70), "constructs DriverImpl");
        addReferenceCallers(currentProgram.getImageBase().add(0x6bbc0), "sets unsupported-mapping callback on device thread");

        String[] imports = {
            "CreateFileA", "CreateFileW", "DeviceIoControl", "ReadFile",
            "RegisterRawInputDevices", "GetRawInputData", "GetRawInputDeviceInfoW"
        };
        for (Function function : currentProgram.getFunctionManager().getFunctions(true)) {
            for (String name : imports) {
                if (function.getName().contains(name)) {
                    addReferenceCallers(function.getEntryPoint(), "calls " + name);
                }
            }
        }

        String[] stringNeedles = {
            "ProbeFilterDriverEndpoint", "driver ready", "fail to connect to driver",
            "fail to probe filter driver", "driver_impl_win.cc", "driver_thread_win.cc",
            "endpoint_impl_win.cc", "hardware_event_endpoint_win.cc",
            "hardware_event_thread_win.cc", "UnsupportedMapping", "unsupported mapping"
        };
        for (Data data : currentProgram.getListing().getDefinedData(true)) {
            Object value = data.getValue();
            if (!(value instanceof String)) {
                continue;
            }
            String text = (String)value;
            for (String needle : stringNeedles) {
                if (text.contains(needle)) {
                    addReferenceCallers(data.getAddress(), "references string: " + text);
                    break;
                }
            }
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try (PrintWriter out = new PrintWriter(new File(args[0]), "UTF-8")) {
            out.println("Image base: " + currentProgram.getImageBase());
            for (Map.Entry<Address, String> item : targets.entrySet()) {
                Function function = getFunctionAt(item.getKey());
                if (function == null) {
                    function = getFunctionContaining(item.getKey());
                }
                out.println("\n===== " + item.getValue() + " =====");
                if (function == null) {
                    out.println("No function at " + item.getKey());
                    continue;
                }
                out.println(function.getName() + " @ " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted()) {
                    out.println(result.getDecompiledFunction().getC());
                } else {
                    out.println("Decompiler failed: " + result.getErrorMessage());
                }
            }
        } finally {
            decompiler.dispose();
        }
    }

    private void addRva(long rva, String reason) {
        targets.put(currentProgram.getImageBase().add(rva), reason);
    }

    private void addReferenceCallers(Address address, String reason) {
        for (Reference reference : currentProgram.getReferenceManager().getReferencesTo(address)) {
            Function caller = getFunctionContaining(reference.getFromAddress());
            if (caller != null) {
                targets.putIfAbsent(caller.getEntryPoint(), reason);
            }
        }
    }
}
