# Especificación técnica — Módulo de detección de Soporte/Resistencia (XAUUSD M15)

| Campo | Valor |
|-------|--------|
| Estado | **Diseño aprobado** |
| Implementación | **Implementada en código (solo detección / mantenimiento / invalidación / selección / visualización)** |
| Instrumento | XAUUSD |
| Timeframe de detección | M15 |
| Versión del documento | 1.8 |
| Última actualización | 2026-11-14 |

Este documento es la especificación de diseño e implementación del módulo de detección de niveles.

**Autorizado:** detección, mantenimiento, invalidación, selección de candidatos y visualización.  
**No autorizado:** entradas, salidas, órdenes, riesgo, position sizing, señales finales LONG/SHORT.

Las reglas de entrada/salida de la estrategia de trading se definirán en un documento separado.

---

## 1. Objetivo

Diseñar e implementar un módulo autónomo de detección de soporte/resistencia para un cBot de cTrader sobre XAUUSD en M15.

El módulo debe detectar, mantener y clasificar niveles horizontales y diagonales de pendiente baja/moderada.

La estrategia de trading anticipa un breakout en el **tercer acercamiento independiente** a un nivel.  
El módulo de detección debe identificar niveles tras **dos toques independientes**, sin exigir un tercer toque ni un breakout confirmado para activarlos.

El módulo permanece independiente de entradas, salidas, gestión de riesgo y tamaño de posición.

---

## 2. Alcance

El módulo debe soportar:

- Soporte y resistencia horizontales.
- Soporte y resistencia diagonales de pendiente baja/moderada.
- Detección basada en swings.
- Conteo de toques independientes.
- Tolerancia de precio basada en ATR.
- Fusión de niveles cercanos.
- Detección de confluencia.
- Invalidación por breakout.
- Retención histórica de niveles invalidados.
- Selección del nivel activo más cercano en la dirección de breakout anticipada.
- Visualización configurable en el gráfico.

---

## 3. Mercado y timeframe

| Parámetro | Valor aprobado |
|-----------|----------------|
| Instrumento | XAUUSD |
| Timeframe | M15 |
| Referencia de volatilidad | ATR(14) calculado en M15 |
| Actualización de niveles / swings / toques | Solo velas M15 **cerradas** |
| Invalidación por breakout y selección de candidatos | En **cada tick** de mercado |

No se permite lookahead ni uso de datos de velas futuras.  
El ATR **nunca** se calcula con la vela en formación.

---

## 4. Detección de swings

Los niveles horizontales se derivan de swing highs y swing lows confirmados.  
Los niveles diagonales se construyen a partir de pares de swings confirmados compatibles.

### Parámetros aprobados

| Parámetro | Valor |
|-----------|--------|
| Fuerza del swing (K) | 3 velas completadas a cada lado |
| Lookback histórico inicial | 480 velas M15 completadas |

### Reglas deterministas

- Un swing high en la barra `j` se confirma solo cuando existen y están cerradas las barras `j−K … j+K`, y `High[j]` es **estrictamente mayor** que los highs del resto de la ventana.
- Un swing low se confirma de forma análoga con `Low[j]` **estrictamente menor**.
- La confirmación nunca usa información futura no disponible en el momento de la detección.
- **Extremos iguales:** si hay empate de extremos dentro de la ventana de comparación K=3, se retiene la **ocurrencia más temprana** (la igualdad en un vecino impide que un extremo posterior sea “estrictamente” mayor/menor; por tanto prevalece el primero que cumpla la condición estricta al confirmarse).

---

## 5. Niveles horizontales

### 5.1 Formación

- Los swing highs generan candidatos de **resistencia**.
- Los swing lows generan candidatos de **soporte**.
- Un toque cualificado crea un candidato **Provisional**.
- **Dos toques independientes** activan el nivel (`Active`).
- No se requiere un tercer toque para la activación.

### 5.2 Zona de precio

```
ZoneTolerance = 0.15 × ATR_ref   (tras Active)
HalfWidth     = 0.15 × ATR_ref   (congelado al pasar a Active)
```

En estado Provisional, la media anchura operativa usa `0.15 × ATR_current` (velas cerradas).

- El centro de una zona horizontal fusionada es la **mediana** de los extremos de swing asociados.
- La zona **no** debe expandirse de forma ilimitada al fusionar candidatos cercanos: el half-width de un Active permanece congelado; no se ensancha para cubrir el rango de extremos.

### Decisión 12 — Solapamiento horizontal inclusivo

Dos zonas horizontales del mismo rol pueden fusionarse si sus intervalos se **solapan o se tocan exactamente** en sus límites.

La comparación de intervalos es **inclusiva**:

```
[LowA, HighA] solapa [LowB, HighB]  ⇔  LowA ≤ HighB  ∧  LowB ≤ HighA
```

---

## 6. Niveles diagonales

### 6.1 Formación

Los niveles diagonales se derivan de **dos anclas** de swing confirmadas y compatibles.

| Regla | Valor |
|-------|--------|
| Separación mínima entre anclas | ≥ 12 velas M15 |
| Soporte y resistencia | Tratados de forma independiente |
| Pendientes | Baja/moderada, en ambas direcciones cuando sea geométricamente coherente |
| Canales empinados | Excluidos (sin construcción de canales) |
| Activación | Dos toques independientes son suficientes |
| Rango de pendiente | **0.5–3 ATR por cada 100 velas M15** |
| Emparejamiento de anclas | **Solo pivotes consecutivos del mismo tipo** (Decisión 13) |

Normalización de pendiente:

```
SlopeNorm = |ΔPrecio| / ΔBarras × 100 / ATR_usado_en_construcción
```

Solo se aceptan diagonales con `SlopeNorm` en `[0.5, 3]`.

#### Decisión 13 — Emparejamiento consecutivo (no todas las parejas)

Al confirmarse un swing nuevo, se intenta formar **como máximo una** diagonal con el **pivote previo del mismo tipo** (mismo `SwingKind`: High→High o Low→Low), si cumple separación mínima y rango de pendiente.

- **No** se empareja el swing nuevo con todos los swings históricos del mismo tipo (eso produce O(N²) diagonales y satura el gráfico).
- Si el pivote previo no cumple separación o pendiente, **no** se crea diagonal para ese swing (no se busca un pivote más antiguo).
- Las dos anclas cuentan como dos toques independientes; el nivel nace en estado **Active** cuando la pareja es válida.
- Cota esperada: número de diagonales creadas ≤ número de swings del mismo tipo (orden lineal), no combinatorio.

### 6.2 Persistencia y geometría fija

- Un nivel diagonal **activo** no expira solo porque sus velas ancla salgan de la ventana de lookback inicial (480).
- Permanece relevante hasta invalidación por breakout.
- Tras la activación, **anclas y pendiente permanecen fijas**.

### 6.3 Fusión de diagonales cercanas

Se pueden fusionar candidatos diagonales cuando:

- Mismo rol (soporte o resistencia).
- Misma dirección de pendiente.
- Diferencia relativa de pendiente ≤ **15%** del mayor valor absoluto de pendiente.
- Separación del precio proyectado actual ≤ **0.15 × ATR_current**.
- Se evita el doble conteo de toques.

**Decisión técnica (fusión Provisional ↔ Active):**  
Nunca modificar los anclajes ni la pendiente de un nivel **Active**.  
Si un Provisional es compatible con un Active, se absorben toques/extremos del Provisional hacia el Active y se descarta el Provisional, **sin** alterar la geometría del Active.

---

## 7. Detección de toques

Un toque se determina con High/Low de la vela, no con el cuerpo.

| Rol | Precio de contacto |
|-----|--------------------|
| Resistencia | High de la vela |
| Soporte | Low de la vela |

Una mecha que entra o alcanza la zona del nivel es suficiente para un toque.

Varias velas consecutivas cerca de la misma zona cuentan como **un solo episodio de toque**.

### 7.1 Separación de toques independientes

Un nuevo toque independiente requiere:

1. Un toque cualificado anterior.
2. Que el precio se aleje del nivel al menos **0.5 × ATR_current** respecto al **borde relevante de la zona**.
3. Que el **Close** de una vela M15 **completada** confirme ese alejamiento (retreat).
4. Que el precio regrese a la zona en una vela **estrictamente posterior** a la que confirmó el retreat.

#### Decisión 14 — Secuencia temporal retreat → toque

- Al confirmar el alejamiento se registra el índice de esa vela (`RetreatConfirmedAtBar`) y se **cierra** el episodio de toque en curso (`InTouchEpisode = false`).
- **No** se permite confirmar retreat y registrar un nuevo toque independiente en la **misma** vela M15 (aunque la mecha re-entre en la zona mientras el Close valida el alejamiento).
- El siguiente toque independiente solo puede ocurrir con `barIndex > RetreatConfirmedAtBar`.
- Umbral de alejamiento: permanece **0.5 × ATR_current** desde el borde relevante.
- **Excepción:** los dos anclajes iniciales de una diagonal siguen contando como dos toques de formación/activación (§6); no exigen retreat intermedio entre anclas. Los toques **posteriores** al segundo ancla sí aplican Decisión 14.

La regla aplica a horizontales y diagonales.  
En diagonales, la distancia se mide respecto al precio proyectado de la línea en esa barra ± half-width.

---

## 8. Consolidación de niveles

### 8.1 Horizontales

Fusionar candidatos horizontales cuando:

- Misma clasificación soporte/resistencia.
- Sus zonas de precio se solapan o se tocan (inclusivo, Decisión 12).

Usar la mediana de los extremos de swing asociados como centro consolidado.  
Mantener identidades de toque independientes y evitar doble conteo.  
No permitir fusión que ensanche el half-width de un Active.

Si se fusiona Provisional con Active horizontal: conservar `ATR_ref` y half-width del Active; recentrar con la mediana de todos los extremos asociados.

### 8.2 Diagonales

Según §6.3 (geometría Active inmutable).

### 8.3 Confluencia horizontal/diagonal

- Horizontal y diagonal permanecen como **objetos separados**.
- La confluencia solo se reconoce entre niveles en estado **Active**.
- **Decisión técnica:** la confluencia se declara cuando las **zonas existentes se solapan** (criterio inclusivo), usando para la diagonal la proyección en la barra/tiempo de evaluación ± su half-width. **No** se introduce un filtro de distancia adicional distinto del solape de zonas.
- La confluencia:
  - puede aumentar la significancia para evaluación posterior;
  - **no** crea un nivel nuevo;
  - **no** cuenta como toque adicional;
  - **no** dispara operaciones.

---

## 9. ATR y geometría de zona

### Decisión 11 — ATR_ref vs ATR_current

| Símbolo | Definición |
|---------|------------|
| `ATR_ref` | Valor congelado al activar un nivel; fija la anchura de zona (`HalfWidth = 0.15 × ATR_ref`) |
| `ATR_current` | Último ATR(14) calculado **exclusivamente** con velas M15 **cerradas** |

Reglas:

1. Congelar `ATR_ref` cuando un nivel pasa a **Active**.
2. Congelar el half-width activo en `0.15 × ATR_ref`.
3. Seguir usando `ATR_current` para niveles nuevos, toques/retreat, fusiones provisionales y **buffer de invalidación**.
4. Buffer de invalidación: `0.10 × ATR_current`.
5. La invalidación se comprueba en cada tick con Bid/Ask.
6. No utilizar ATR calculado con la vela en formación.
7. No permitir expansión ilimitada de zona durante fusiones.

---

## 10. Ciclo de vida del nivel

| Estado | Significado |
|--------|-------------|
| `Provisional` | Toques independientes insuficientes (&lt; 2) |
| `Active` | ≥ 2 toques independientes confirmados |
| `Invalidated` | Breakout confirmado según la regla tick (o proxy OHLC documentado en replay) |

Niveles invalidados:

- Quedan excluidos de candidatos de entrada futuros.
- Permanecen en el registro histórico.
- **No** invierten automáticamente su rol soporte/resistencia.

Si el precio se acerca pero revierte sin tocar ni romper el nivel, el nivel permanece Active.

Retests y flips de rol están **explícitamente excluidos**.

---

## 11. Invalidación por breakout (tick)

La detección de breakout se ejecuta en **cada tick** de mercado, **sin esperar** el cierre de la vela M15.

```
BreakoutBuffer = 0.10 × ATR_current
```

### Resistencia

```
Bid > UpperZoneBoundary + 0.10 × ATR_current
```

### Soporte

```
Ask < LowerZoneBoundary − 0.10 × ATR_current
```

Para diagonales, los límites se calculan a partir de la **proyección de la línea en el tiempo del tick actual**.

Al invalidar:

1. Transición inmediata a `Invalidated`.
2. Exclusión de candidatos de entrada futuros.
3. Retención de información histórica.
4. Nunca invertir el rol del nivel.
5. No operar retests.

### Proxy OHLC en replay histórico (limitación)

Durante la reconstrucción solo-OHLC (sin ticks):

- No existe Bid/Ask histórico tick a tick.
- Se aplica un **proxy determinista por cierre de vela**: misma geometría de bordes, pero usando `Close` de la vela cerrada en lugar de Bid/Ask.
- Este proxy **no** reproduce invalidaciones intradía exactas.
- Tras el arranque en tiempo real, la invalidación pasa a Bid/Ask en cada tick.

---

## 12. Selección de candidatos (tick)

### 12.1 Candidatos del motor (nearest Active)

En cada tick el motor expone el nivel Active más cercano por lado (cualquier `IndependentTouchCount ≥ 2`):

| Dirección analizada | Candidato |
|---------------------|-----------|
| Breakout alcista / LONG | Resistencia **Active** más cercana **por encima** del Ask actual |
| Breakout bajista / SHORT | Soporte **Active** más cercano **por debajo** del Bid actual |

**Empates:** si dos niveles Active están a la misma distancia, elegir el de **identificador más antiguo** (Id menor). Criterio estable y determinista.

- Se conservan todos los demás niveles Active.
- La selección de candidatos **no** autoriza ejecución de órdenes.

### 12.2 Capa de candidatos de entrada estructurales (T=2, antes del tercer toque)

Capa **independiente** de solo lectura (`EntryCandidateSelector`). **No** modifica el motor de detección, estados, contadores, ATR, fusiones ni invalidación.

Un nivel es **candidato de entrada estructural** si cumple **simultáneamente**:

1. `State == Active`.
2. `IndependentTouchCount == 2` (exactamente dos toques independientes).
3. No está invalidado (`Invalidation == null`).
4. Resistencia situada **por encima del Ask** → etiqueta visual `LONG CANDIDATE` (futuro LONG).
5. Soporte situado **por debajo del Bid** → etiqueta visual `SHORT CANDIDATE` (futuro SHORT).

Reglas adicionales:

- Los niveles Active con `T ≥ 3` **permanecen** en el motor y pueden seguir siendo nearest del §12.1, pero **no** son candidatos de esta capa.
- Provisional e Invalidated nunca son candidatos.
- Resistencia por debajo del Ask → no candidato LONG.
- Soporte por encima del Bid → no candidato SHORT.
- Horizontales y diagonales aplican las **mismas** reglas (precio proyectado en la barra de referencia para diagonales).
- Se listan **todos** los niveles que cumplen (ordenados por distancia; empate → Id menor), no solo el más cercano.

**Alcance explícito de esta capa:** solo identifica candidatos estructurales previos al tercer toque anticipado. **No** define el momento de entrada, **no** confirma una ruptura y **no** genera órdenes, Stop Loss, Take Profit ni flechas de ejecución.

Parámetro de visualización: `Show Entry Candidates` (Yes/No). Si está activo y no hay candidatos, se muestra un mensaje informativo (sin inventar señales). Con `Show Touches = Yes`, se mantienen visibles los toques históricos T1 y T2 de esos candidatos.

---

## 13. Arquitectura del módulo

Separación obligatoria de responsabilidades:

1. **Detección de swings** (velas cerradas).
2. **Construcción de niveles** horizontales y diagonales (velas cerradas).
3. **Gestión de toques independientes** (velas cerradas).
4. **Fusión de niveles** (velas cerradas).
5. **Detección de confluencia** (tras actualización de Active).
6. **Ciclo de vida e invalidación** (invalidación en tick; proxy OHLC en replay).
7. **Selección de candidatos del motor** (tick; nearest Active, §12.1).
8. **Candidatos de entrada estructurales T=2** (capa independiente de solo lectura; §12.2).
9. **Replay histórico y recuperación de estado** (arranque).
10. **Visualización** (configurable; no altera reglas).

### Flujo lógico

```text
Velas M15 cerradas + ATR(14)
        │
        ▼
┌─────────────────────┐
│ 1. SwingDetection   │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 2. LevelConstruction│  (H + D)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 3. TouchManagement  │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 4. LevelMerging     │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 5. Confluence       │  (solo Active; solape de zonas)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 6. Lifecycle        │◄── invalidación tick (Bid/Ask + 0.10×ATR_current)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 7. CandidateSelect  │◄── cada tick (nearest Active, §12.1)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 8. EntryCandidates  │  (T=2 lado correcto; solo lectura, §12.2)
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 9. Visualization    │  (opcional; Show Entry Candidates)
└─────────────────────┘
```

### Información estructurada expuesta

Cada nivel debe poder exponer al menos:

- Identificador único.
- Clasificación horizontal/diagonal.
- Clasificación soporte/resistencia.
- Precio centro o precio de línea proyectado.
- Límites de zona / tolerancia.
- Puntos ancla.
- Conteo de toques independientes.
- Estado de ciclo de vida.
- Relaciones de confluencia.
- Marcas temporales de creación y última actualización.
- Información de invalidación, si aplica.

El diseño debe permitir replay histórico determinista sin lookahead.

---

## 14. Recuperación de estado (v1)

Reglas aprobadas para la versión 1:

- Reconstruir el estado de niveles mediante **replay histórico determinista** al arrancar.
- **Sin** almacenamiento persistente de niveles en archivo en v1.
- Lookback de búsqueda inicial: **480** velas M15 cerradas.
- **Precarga adicional** cuando sea necesaria para confirmar swings (K) y calentar ATR(14).  
  No se afirma que 480 velas permitan reconstruir niveles de antigüedad ilimitada: solo se analiza la ventana de lookback (+ precarga técnica).
- Informar si el historial disponible es insuficiente.
- Limitación OHLC vs ticks: ver §11 (proxy de cierre; sin precisión tick intradía).

---

## 15. Frontera con la estrategia de trading

La estrategia futura pretende entrar **antes del tercer toque anticipado**, esperando breakout y continuación.

La capa §12.2 (`EntryCandidateSelector`) solo marca niveles estructurales Active con `T=2` en el lado correcto del precio. **No** define todavía el momento de entrada ni confirma una ruptura.

El módulo de detección **no debe**:

- Abrir operaciones.
- Generar señales finales de entrada.
- Esperar un breakout confirmado antes de una entrada (eso es lógica de estrategia, documento aparte).
- Implementar stop-loss, take-profit o gestión de riesgo.
- Operar retests.
- Invertir automáticamente roles soporte/resistencia.

Las reglas de entrada y salida se especificarán por separado.

---

## 16. Decisiones técnicas de implementación (cerradas)

| Tema | Decisión |
|------|----------|
| ATR invalidación | `0.10 × ATR_current` (Decisión 11) |
| Solape horizontal | Inclusivo, incluyendo contacto exacto (Decisión 12) |
| Emparejamiento diagonal | Solo pivote previo del mismo tipo; no todas las parejas (Decisión 13) |
| Secuencia retreat→toque | Toque solo en vela posterior a `RetreatConfirmedAtBar` (Decisión 14) |
| Fusión D Provisional→Active | Absorber toques; **no** mover anclas/pendiente Active |
| Confluencia H↔D | Solape de zonas existentes; sin filtro extra |
| Historial | Lookback 480 + precarga ATR/K; sin antigüedad ilimitada |
| Empate candidatos | Id más antiguo (menor) |
| Replay invalidación | Proxy por `Close` de vela cerrada; ticks reales tras arranque |

No quedan ambigüedades abiertas que bloqueen la implementación del módulo de detección.

---

## 17. Exclusiones explícitas

No implementar en el alcance de este módulo:

- Señales LONG/SHORT de entrada.
- Trading de retest.
- Inversión automática de rol S/R.
- Stop-loss / take-profit.
- Gestión de riesgo o position sizing.
- Ejecución de órdenes.
- Indicadores o filtros adicionales no especificados aquí.
- Optimización o backtesting de rentabilidad.

---

## 18. Visualización

Visualización **configurable**, sin alterar reglas de detección, estados, toques, fusiones ni invalidación.

Parámetro principal: `Visualization Mode`.

### 18.1 Modo Trading View (predeterminado)

Estilo limpio orientado a lectura operativa:

- Solo niveles en estado **Active**.
- Hasta **3 resistencias** más cercanas **por encima del Ask**.
- Hasta **3 soportes** más cercanos **por debajo del Bid**.
- Horizontales y diagonales compiten en la **misma** métrica de distancia al Bid/Ask (precio proyectado de la diagonal en la barra de referencia).
- Empate de distancia → Id más antiguo (menor), igual que la selección de candidatos del motor.
- Diagonales: **una sola línea** (sin banda ni rectángulo de tolerancia).
- Horizontales: línea central (sin relleno de zona).
- Etiquetas solo para los niveles seleccionados.
- **`Show Touches`:** si está activo, dibuja los `TouchRecord` independientes de esos niveles seleccionados en su `Time`/`Price` originales, con etiqueta `#Id Tn` (p. ej. `#70 T1`). Si está desactivado, no se dibuja ningún marcador de toque.
- Al refrescar se hace `Clear` de objetos `SR_*` previos y se redibuja el conjunto actual → niveles y toques que dejan de estar seleccionados se eliminan.
- La selección se recalcula tras invalidación/candidatos en tick; el redibujo ocurre cuando cambia el conjunto de Ids o periódicamente (proyección).

El filtrado es **solo visual** (`TradingViewLevelSelector` + `TradingViewTouchMarkers`). El motor conserva todos los niveles y toques.

### 18.2 Modo Full Debug

Visualización completa de inspección:

- Zonas horizontales y diagonales con bandas de tolerancia.
- Diferenciación Active / Provisional / Invalidated (según flags).
- Identificador, toques independientes y confluencia.
- Marcadores de swings y toques cuando los parámetros lo permiten (comportamiento previo; círculos en todos los niveles visibles según flags).
- Resaltado de candidatos del motor (nearest R / nearest S).

Flags: `Show Invalidated`, `Show Swings`, `Show Touches`, `Show Confluence`, `Show Entry Candidates`.  
`Show Touches` aplica en **ambos** modos; en Trading View solo sobre la selección ≤6 niveles (y sobre candidatos de entrada si `Show Entry Candidates` está activo).

### 18.3 Candidatos de entrada (`Show Entry Candidates`)

Capa visual independiente (§12.2), aplicable sobre Trading View o Full Debug:

- Identifica niveles Active con `T=2` en el lado correcto del precio.
- Etiquetas `LONG CANDIDATE` (resistencias) y `SHORT CANDIDATE` (soportes).
- Estilos diferenciados (grosor/estilo/color distintos al resto de niveles).
- Con `Show Touches = Yes`, mantiene visibles T1 y T2 históricos de esos candidatos.
- Si no hay candidatos: mensaje informativo; **sin** inventar señales.
- **No** dibuja flechas de compra/venta ni señales de ejecución.

**Anclaje de etiquetas (v1.8):** el cálculo de candidatos sigue siendo exclusivamente M15. La posición X de las etiquetas `LONG/SHORT CANDIDATE` (y del mensaje vacío) usa el viewport del `Chart` (`FirstVisibleBarIndex` / `LastVisibleBarIndex` sobre `Chart.Bars`), con un inset desde el borde derecho (`ChartVisibleLabelAnchor`), para que sean visibles en **M15 y H1**. Cambiar el timeframe del gráfico no altera niveles ni candidatos; solo la posición gráfica. Los marcadores T1/T2 conservan su `Time`/`Price` histórico. Cada refresh hace `Clear` de objetos `SR_*` previos antes de redibujar (sin duplicados).

### 18.4 Parámetros

| Parámetro | Default | Notas |
|-----------|---------|--------|
| `Enable Visualization` | true | Master on/off |
| `Visualization Mode` | `TradingView` | `TradingView` \| `FullDebug` |
| `Show Invalidated` | true | Full Debug |
| `Show Swings` | true | Full Debug |
| `Show Touches` | true | Trading View (selección), Full Debug y candidatos de entrada |
| `Show Confluence` | true | Full Debug |
| `Show Entry Candidates` | true | Capa T=2 estructural (§12.2); no implica entrada |

---

## 19. Estado de aprobación

| Concepto | Estado |
|----------|--------|
| Especificación de diseño | **Aprobada** (v1.8: anclaje de etiquetas al viewport del Chart) |
| Autorización de implementación del módulo S/R | **Aprobada e implementada** (detección / visualización / candidatos estructurales) |
| Corrección saturación S/R | **Aplicada** — emparejamiento diagonal O(N) (Decisión 13) |
| Modo visual Trading View | **Implementado** (filtrado visual + toques opcionales; motor intacto) |
| Candidatos de entrada T=2 | **Implementados** (capa independiente; sin timing de entrada ni ruptura) |
| Etiquetas visibles M15/H1 | **Corregido** — anclaje a barras visibles del Chart; cálculo sigue en M15 |
| Señales de entrada / órdenes | **No autorizadas / no implementadas** |
| Commit / push | No realizados por la tarea de implementación |

## 20. Mapa de código (implementación)

| Componente | Ubicación |
|------------|-----------|
| Modelos | `XauBreakoutBot/SupportResistance/SrModels.cs` |
| ATR | `XauBreakoutBot/SupportResistance/AtrCalculator.cs` |
| Swings | `XauBreakoutBot/SupportResistance/SwingDetector.cs` |
| Motor S/R | `XauBreakoutBot/SupportResistance/SupportResistanceEngine.cs` |
| Modo visual | `XauBreakoutBot/SupportResistance/VisualizationMode.cs` |
| Selector Trading View | `XauBreakoutBot/SupportResistance/TradingViewLevelSelector.cs` |
| Marcadores toque TV | `XauBreakoutBot/SupportResistance/TradingViewTouchMarkers.cs` |
| Candidatos entrada T=2 | `XauBreakoutBot/SupportResistance/EntryCandidateSelector.cs` |
| Anclaje etiquetas viewport | `XauBreakoutBot/SupportResistance/ChartVisibleLabelAnchor.cs` |
| Visualización | `XauBreakoutBot/SupportResistance/LevelChartVisualizer.cs` |
| cBot host | `XauBreakoutBot/XauBreakoutBot.cs` |
| Pruebas unitarias | `XauBreakoutBot.Tests/SupportResistanceEngineTests.cs` |
| Pruebas selector visual | `XauBreakoutBot.Tests/TradingViewLevelSelectorTests.cs` |
| Pruebas marcadores toque | `XauBreakoutBot.Tests/TradingViewTouchMarkersTests.cs` |
| Pruebas candidatos entrada | `XauBreakoutBot.Tests/EntryCandidateSelectorTests.cs` |
| Pruebas anclaje etiquetas | `XauBreakoutBot.Tests/ChartVisibleLabelAnchorTests.cs` |

### Visualización en cTrader

1. Compilar el proyecto (se publica `XauBreakoutBot.algo` en Sources/Robots).
2. En cTrader, abrir XAUUSD (preferible gráfico M15).
3. Añadir la instancia del cBot `XauBreakoutBot`.
4. Parámetros: `Enable Visualization`, `Visualization Mode` (`TradingView` / `FullDebug`), flags Full Debug (`Show Invalidated`, `Show Swings`, `Show Touches`, `Show Confluence`) y `Show Entry Candidates`.
5. Iniciar el cBot: al arranque hace replay OHLC; en tick actualiza invalidación Bid/Ask y candidatos; el modo Trading View redibuja al cambiar la selección; la capa T=2 resalta candidatos estructurales sin señales de ejecución.
6. Revisar el log: advertencias de historial insuficiente y la limitación del proxy OHLC.

**Nota de build:** los parámetros y `[Robot]` se declaran en código. No se usa `config.json` como AdditionalFiles (conflicto CGEN006 con atributos). Puede aparecer el warning CGEN001 (config no encontrado); es aceptable.
